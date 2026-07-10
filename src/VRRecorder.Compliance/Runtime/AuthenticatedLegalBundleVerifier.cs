using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Runtime;

public sealed class AuthenticatedLegalBundleVerifier
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private const string ManifestFileName = "LEGAL-MANIFEST.sha256";
    private const string CatalogFileName = "THIRD-PARTY-COMPONENTS.json";
    private readonly IAuthenticatedLegalBundleAnchorSource _anchors;

    public AuthenticatedLegalBundleVerifier(
        IAuthenticatedLegalBundleAnchorSource anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        _anchors = anchors;
    }

    public async Task<LegalBundleVerification> VerifyAsync(
        string bundleDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        AuthenticatedLegalBundleAnchor anchor;
        try
        {
            anchor = await _anchors
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticatedLegalBundleAnchorUnavailableException)
        {
            return Reject(
                "legal-bundle-authenticated-anchor-missing",
                "authenticated-manifest-anchor");
        }

        var root = Path.GetFullPath(bundleDirectory);
        var manifestPath = Path.Combine(root, ManifestFileName);
        var catalogPath = Path.Combine(root, CatalogFileName);
        if (!File.Exists(manifestPath) || !File.Exists(catalogPath))
        {
            return Reject("legal-bundle-missing", root);
        }

        if (ContainsReparsePoint(root, ManifestFileName))
        {
            return Reject("legal-bundle-reparse-point", ManifestFileName);
        }

        var manifestBytes = await File
            .ReadAllBytesAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);
        var manifestDigest = SHA256.HashData(manifestBytes);

        if (!CryptographicOperations.FixedTimeEquals(
                manifestDigest,
                Convert.FromHexString(anchor.ManifestSha256)))
        {
            return Reject("legal-bundle-manifest-digest-mismatch", ManifestFileName);
        }

        var payloadIssue = await VerifyPayloadsAsync(
                root,
                manifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (payloadIssue is not null)
        {
            return new LegalBundleVerification.Rejected([payloadIssue]);
        }

        try
        {
            await using var catalog = new FileStream(
                catalogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument
                .ParseAsync(
                    catalog,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 16,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            var catalogRoot = document.RootElement;
            if (HasDuplicateProperties(catalogRoot))
            {
                return Reject("legal-bundle-catalog-invalid", CatalogFileName);
            }

            var schemaVersion = catalogRoot
                .GetProperty("schemaVersion")
                .GetInt32();
            var bundleId = catalogRoot
                .GetProperty("bundleId")
                .GetString();
            var integrity = catalogRoot.GetProperty("integrityManifest");
            var manifestReference = integrity.GetProperty("path").GetString();
            var algorithm = integrity.GetProperty("algorithm").GetString();
            if (schemaVersion != 2 ||
                !string.Equals(bundleId, anchor.BundleId, StringComparison.Ordinal) ||
                !string.Equals(
                    manifestReference,
                    ManifestFileName,
                    StringComparison.Ordinal) ||
                !string.Equals(algorithm, "SHA-256", StringComparison.Ordinal))
            {
                return Reject("legal-bundle-identity-mismatch", CatalogFileName);
            }
        }
        catch (Exception exception) when (
            exception is JsonException or
                InvalidOperationException or
                KeyNotFoundException)
        {
            return Reject("legal-bundle-catalog-invalid", CatalogFileName);
        }

        return new LegalBundleVerification.Verified(
            new LegalBundleIdentity(
                anchor.BundleId,
                anchor.ManifestSha256));
    }

    private static LegalBundleVerification.Rejected Reject(
        string code,
        string subject) =>
        new([new ComplianceIssue(code, subject)]);

    private static async Task<ComplianceIssue?> VerifyPayloadsAsync(
        string root,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        List<ManifestEntry> entries;
        try
        {
            entries = ParseManifest(manifestBytes, root);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                DecoderFallbackException or
                FormatException)
        {
            return new ComplianceIssue(
                "legal-bundle-manifest-invalid",
                ManifestFileName);
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(entry.FullPath))
            {
                return new ComplianceIssue(
                    "legal-bundle-payload-missing",
                    entry.RelativePath);
            }

            if (ContainsReparsePoint(root, entry.RelativePath))
            {
                return new ComplianceIssue(
                    "legal-bundle-reparse-point",
                    entry.RelativePath);
            }

            await using var payload = new FileStream(
                entry.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actualHash = await SHA256
                .HashDataAsync(payload, cancellationToken)
                .ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    actualHash,
                    entry.ExpectedHash))
            {
                return new ComplianceIssue(
                    "legal-bundle-payload-hash-mismatch",
                    entry.RelativePath);
            }
        }

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var expectedPaths = entries
            .Select(entry => entry.RelativePath)
            .Append(ManifestFileName)
            .ToHashSet(pathComparer);
        var unexpectedPath = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path
                .GetRelativePath(root, path)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/'))
            .Where(path => !expectedPaths.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (unexpectedPath is not null)
        {
            return new ComplianceIssue(
                "legal-bundle-payload-unexpected",
                unexpectedPath);
        }

        return null;
    }

    private static List<ManifestEntry> ParseManifest(
        byte[] manifestBytes,
        string root)
    {
        var manifest = StrictUtf8.GetString(manifestBytes);
        if (manifest.Length == 0 ||
            !manifest.EndsWith('\n') ||
            manifest.Contains('\r'))
        {
            throw new FormatException(
                "The legal manifest must use canonical LF-terminated lines.");
        }

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var paths = new HashSet<string>(pathComparer);
        var entries = new List<ManifestEntry>();
        foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
            {
                throw new FormatException("A legal manifest line is malformed.");
            }

            var hashText = line[..64];
            if (hashText.Any(character =>
                    character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            {
                throw new FormatException("A legal manifest hash is malformed.");
            }

            var relativePath = line[66..];
            if (string.Equals(
                    relativePath,
                    ManifestFileName,
                    StringComparison.Ordinal) ||
                !paths.Add(relativePath))
            {
                throw new FormatException(
                    "A legal manifest path is duplicated or self-referential.");
            }

            entries.Add(new ManifestEntry(
                relativePath,
                LegalArtifactPath.Resolve(root, relativePath),
                Convert.FromHexString(hashText)));
        }

        return entries;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!propertyNames.Add(property.Name) ||
                    HasDuplicateProperties(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasDuplicateProperties(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsReparsePoint(
        string root,
        string relativePath)
    {
        var current = root;
        foreach (var segment in relativePath.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ManifestEntry(
        string RelativePath,
        string FullPath,
        byte[] ExpectedHash);
}
