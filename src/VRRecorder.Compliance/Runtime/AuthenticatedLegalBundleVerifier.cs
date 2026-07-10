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
        var anchor = await _anchors
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        var root = Path.GetFullPath(bundleDirectory);
        var manifestPath = Path.Combine(root, ManifestFileName);
        var catalogPath = Path.Combine(root, CatalogFileName);
        if (!File.Exists(manifestPath) || !File.Exists(catalogPath))
        {
            return Reject("legal-bundle-missing", root);
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

    private sealed record ManifestEntry(
        string RelativePath,
        string FullPath,
        byte[] ExpectedHash);
}
