using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Runtime;

public sealed class AuthenticatedLegalCatalogReader : ILegalCatalogReader
{
    private const string CatalogFileName = "THIRD-PARTY-COMPONENTS.json";
    private const string ManifestFileName = "LEGAL-MANIFEST.sha256";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly string _bundleDirectory;
    private readonly AuthenticatedLegalBundleVerifier _verifier;

    public AuthenticatedLegalCatalogReader(
        string bundleDirectory,
        AuthenticatedLegalBundleVerifier verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        ArgumentNullException.ThrowIfNull(verifier);
        _bundleDirectory = Path.GetFullPath(bundleDirectory);
        _verifier = verifier;
    }

    public async Task<LegalCatalogReadResult> ReadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return new LegalCatalogReadResult.Available(
                await LoadAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (LegalCatalogRejectedException exception)
        {
            return new LegalCatalogReadResult.Rejected(exception.Issues);
        }
        catch (Exception exception) when (IsFailClosedReadFailure(exception))
        {
            return RejectCatalog("legal-catalog-unreadable", CatalogFileName);
        }
    }

    public async Task<LegalTextReadResult> ReadLicenseTextAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        try
        {
            var loaded = await LoadWithManifestAsync(cancellationToken)
                .ConfigureAwait(false);
            var component = loaded.Catalog.Components.SingleOrDefault(item =>
                string.Equals(item.Id, componentId, StringComparison.Ordinal));
            if (component is null)
            {
                return RejectText(
                    "legal-catalog-component-not-found",
                    componentId);
            }

            var content = await ReadVerifiedPayloadAsync(
                    component.LicenseTextPath,
                    loaded.Manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            string text;
            try
            {
                text = StrictUtf8.GetString(content);
            }
            catch (DecoderFallbackException)
            {
                return RejectText(
                    "legal-catalog-license-text-invalid",
                    component.LicenseTextPath);
            }

            return new LegalTextReadResult.Available(new LegalTextDocument(
                component.Id,
                component.LicenseTextPath,
                text));
        }
        catch (LegalCatalogRejectedException exception)
        {
            return new LegalTextReadResult.Rejected(exception.Issues);
        }
        catch (Exception exception) when (IsFailClosedReadFailure(exception))
        {
            return RejectText(
                "legal-catalog-license-text-unreadable",
                componentId);
        }
    }

    private async Task<LegalCatalogSnapshot> LoadAsync(
        CancellationToken cancellationToken) =>
        (await LoadWithManifestAsync(cancellationToken).ConfigureAwait(false))
        .Catalog;

    private async Task<LoadedCatalog> LoadWithManifestAsync(
        CancellationToken cancellationToken)
    {
        var verification = await _verifier
            .VerifyAsync(_bundleDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (verification is LegalBundleVerification.Rejected rejected)
        {
            throw new LegalCatalogRejectedException(rejected.Issues
                .Select(issue => new LegalCatalogIssue(
                    issue.Code,
                    issue.Subject))
                .ToArray());
        }

        var verified = (LegalBundleVerification.Verified)verification;
        var manifest = await ReadAuthenticatedManifestAsync(
                verified.Identity,
                cancellationToken)
            .ConfigureAwait(false);
        var catalogBytes = await ReadVerifiedPayloadAsync(
                CatalogFileName,
                manifest,
                cancellationToken)
            .ConfigureAwait(false);
        var catalog = ParseCatalog(
            catalogBytes,
            verified.Identity,
            manifest);
        return new LoadedCatalog(catalog, manifest);
    }

    private async Task<IReadOnlyDictionary<string, byte[]>>
        ReadAuthenticatedManifestAsync(
            LegalBundleIdentity identity,
            CancellationToken cancellationToken)
    {
        RejectReparsePoints(ManifestFileName);
        var path = LegalArtifactPath.Resolve(
            _bundleDirectory,
            ManifestFileName);
        var bytes = await File
            .ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        var actualHash = SHA256.HashData(bytes);
        if (!CryptographicOperations.FixedTimeEquals(
                actualHash,
                Convert.FromHexString(identity.ManifestSha256)))
        {
            throw Reject(
                "legal-bundle-manifest-digest-mismatch",
                ManifestFileName);
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Reject("legal-bundle-manifest-invalid", ManifestFileName);
        }

        if (text.Length == 0 ||
            !text.EndsWith('\n') ||
            text.Contains('\r'))
        {
            throw Reject("legal-bundle-manifest-invalid", ManifestFileName);
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var entries = new Dictionary<string, byte[]>(comparer);
        foreach (var line in text.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
            {
                throw Reject(
                    "legal-bundle-manifest-invalid",
                    ManifestFileName);
            }

            var hash = line[..64];
            var relativePath = line[66..];
            try
            {
                _ = LegalArtifactPath.Resolve(
                    _bundleDirectory,
                    relativePath);
                if (hash.Any(character =>
                        character is not (>= '0' and <= '9' or
                            >= 'a' and <= 'f')) ||
                    !entries.TryAdd(
                        relativePath,
                        Convert.FromHexString(hash)))
                {
                    throw new FormatException();
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or FormatException)
            {
                throw Reject(
                    "legal-bundle-manifest-invalid",
                    ManifestFileName);
            }
        }

        return entries;
    }

    private async Task<byte[]> ReadVerifiedPayloadAsync(
        string relativePath,
        IReadOnlyDictionary<string, byte[]> manifest,
        CancellationToken cancellationToken)
    {
        if (!manifest.TryGetValue(relativePath, out var expectedHash))
        {
            throw Reject(
                "legal-catalog-license-reference-invalid",
                relativePath);
        }

        string path;
        try
        {
            path = LegalArtifactPath.Resolve(_bundleDirectory, relativePath);
            RejectReparsePoints(relativePath);
        }
        catch (ArgumentException)
        {
            throw Reject(
                "legal-catalog-license-reference-invalid",
                relativePath);
        }

        if (!File.Exists(path))
        {
            throw Reject("legal-bundle-payload-missing", relativePath);
        }

        var bytes = await File
            .ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(bytes),
                expectedHash))
        {
            throw Reject(
                "legal-bundle-payload-hash-mismatch",
                relativePath);
        }

        return bytes;
    }

    private LegalCatalogSnapshot ParseCatalog(
        byte[] bytes,
        LegalBundleIdentity identity,
        IReadOnlyDictionary<string, byte[]> manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(
                StrictUtf8.GetString(bytes),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                HasDuplicateProperties(root) ||
                RequiredInt32(root, "schemaVersion") != 2 ||
                !string.Equals(
                    RequiredString(root, "bundleId"),
                    identity.BundleId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException();
            }

            var productVersion = RequiredString(root, "productVersion");
            var generatedAt = RequiredString(root, "generatedAtUtc");
            if (!DateTimeOffset.TryParseExact(
                    generatedAt,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out _))
            {
                throw new InvalidDataException();
            }

            var integrity = RequiredObject(root, "integrityManifest");
            if (!string.Equals(
                    RequiredString(integrity, "path"),
                    ManifestFileName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    RequiredString(integrity, "algorithm"),
                    "SHA-256",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException();
            }

            var componentArray = RequiredArray(root, "components");
            var componentIds = new HashSet<string>(StringComparer.Ordinal);
            var components = new List<LegalCatalogComponent>();
            foreach (var element in componentArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException();
                }

                var id = RequiredString(element, "id");
                if (!componentIds.Add(id))
                {
                    throw new InvalidDataException();
                }

                var licenseText = RequiredString(element, "licenseText");
                ValidateLicenseReference(licenseText, manifest);
                components.Add(new LegalCatalogComponent(
                    id,
                    RequiredString(element, "displayName"),
                    RequiredString(element, "version"),
                    RequiredString(element, "licenseExpression"),
                    RequiredString(element, "usage"),
                    RequiredString(element, "linkage"),
                    RequiredBoolean(element, "modified"),
                    RequiredString(element, "sourceInfo"),
                    licenseText));
            }

            return new LegalCatalogSnapshot(
                identity.BundleId,
                productVersion,
                components
                    .OrderBy(component => component.Id, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (LegalCatalogRejectedException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or
                DecoderFallbackException or
                InvalidDataException or
                InvalidOperationException or
                KeyNotFoundException)
        {
            throw Reject("legal-catalog-invalid", CatalogFileName);
        }
    }

    private void ValidateLicenseReference(
        string relativePath,
        IReadOnlyDictionary<string, byte[]> manifest)
    {
        try
        {
            _ = LegalArtifactPath.Resolve(_bundleDirectory, relativePath);
        }
        catch (ArgumentException)
        {
            throw Reject(
                "legal-catalog-license-reference-invalid",
                relativePath);
        }

        if (!relativePath.StartsWith("LICENSES/", StringComparison.Ordinal) ||
            !relativePath.EndsWith("/LICENSE.txt", StringComparison.Ordinal) ||
            !manifest.ContainsKey(relativePath))
        {
            throw Reject(
                "legal-catalog-license-reference-invalid",
                relativePath);
        }
    }

    private void RejectReparsePoints(string relativePath)
    {
        var current = _bundleDirectory;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            throw Reject("legal-bundle-reparse-point", relativePath);
        }

        foreach (var segment in relativePath.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw Reject("legal-bundle-reparse-point", relativePath);
            }
        }
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!properties.Add(property.Name) ||
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

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException();
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException();
        }

        return text;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException();
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException(),
        };
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException();
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException();
    }

    private static LegalCatalogRejectedException Reject(
        string code,
        string subject) =>
        new([new LegalCatalogIssue(code, subject)]);

    private static LegalCatalogReadResult.Rejected RejectCatalog(
        string code,
        string subject) =>
        new([new LegalCatalogIssue(code, subject)]);

    private static LegalTextReadResult.Rejected RejectText(
        string code,
        string subject) =>
        new([new LegalCatalogIssue(code, subject)]);

    private static bool IsFailClosedReadFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            DecoderFallbackException or
            CryptographicException;

    private sealed record LoadedCatalog(
        LegalCatalogSnapshot Catalog,
        IReadOnlyDictionary<string, byte[]> Manifest);

    private sealed class LegalCatalogRejectedException(
        IReadOnlyList<LegalCatalogIssue> issues) : Exception
    {
        public IReadOnlyList<LegalCatalogIssue> Issues { get; } = issues;
    }
}
