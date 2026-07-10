using System.Security.Cryptography;
using System.Text.Json;

namespace VRRecorder.Compliance.Runtime;

public sealed class AuthenticatedLegalBundleVerifier
{
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

        byte[] manifestDigest;
        await using (var manifest = new FileStream(
                         manifestPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 81920,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            manifestDigest = await SHA256
                .HashDataAsync(manifest, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CryptographicOperations.FixedTimeEquals(
                manifestDigest,
                Convert.FromHexString(anchor.ManifestSha256)))
        {
            return Reject("legal-bundle-manifest-digest-mismatch", ManifestFileName);
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
}
