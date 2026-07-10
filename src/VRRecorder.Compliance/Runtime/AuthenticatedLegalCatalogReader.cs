using System.Security.Cryptography;
using System.Text;
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
    private readonly LegalBundleVerificationScope _verificationScope;

    public AuthenticatedLegalCatalogReader(
        string bundleDirectory,
        AuthenticatedLegalBundleVerifier verifier)
        : this(
            bundleDirectory,
            verifier,
            LegalBundleVerificationScope.StrictIsolatedBundle)
    {
    }

    public AuthenticatedLegalCatalogReader(
        string bundleDirectory,
        AuthenticatedLegalBundleVerifier verifier,
        LegalBundleVerificationScope verificationScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        ArgumentNullException.ThrowIfNull(verifier);
        if (!Enum.IsDefined(verificationScope))
        {
            throw new ArgumentOutOfRangeException(nameof(verificationScope));
        }

        _bundleDirectory = Path.GetFullPath(bundleDirectory);
        _verifier = verifier;
        _verificationScope = verificationScope;
    }

    public async Task<LegalCatalogReadResult> ReadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return new LegalCatalogReadResult.Available(
                (await LoadWithManifestAsync(cancellationToken)
                    .ConfigureAwait(false)).Catalog);
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

    public Task<LegalTextReadResult> ReadDocumentAsync(
        string componentId,
        LegalDocumentReference reference,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(reference);
        return ReadDocumentCoreAsync(
            componentId,
            reference,
            useCatalogLicenseReference: false,
            cancellationToken);
    }

    public Task<LegalTextReadResult> ReadLicenseTextAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        return ReadDocumentCoreAsync(
            componentId,
            requestedReference: null,
            useCatalogLicenseReference: true,
            cancellationToken);
    }

    private async Task<LegalTextReadResult> ReadDocumentCoreAsync(
        string componentId,
        LegalDocumentReference? requestedReference,
        bool useCatalogLicenseReference,
        CancellationToken cancellationToken)
    {
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

            LegalDocumentReference reference;
            if (useCatalogLicenseReference)
            {
                reference = component.LegalDocuments.Single(item =>
                    item.Kind == LegalDocumentKind.License);
            }
            else
            {
                reference = component.LegalDocuments.SingleOrDefault(item =>
                    item == requestedReference)!;
                if (reference is null)
                {
                    return RejectText(
                        "legal-catalog-document-reference-mismatch",
                        ReferenceSubject(componentId, requestedReference!));
                }
            }

            var content = await ReadVerifiedPayloadAsync(
                    reference.RelativePath,
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
                    "legal-catalog-document-text-invalid",
                    reference.RelativePath);
            }

            return new LegalTextReadResult.Available(new LegalTextDocument(
                component.Id,
                reference,
                text));
        }
        catch (LegalCatalogRejectedException exception)
        {
            return new LegalTextReadResult.Rejected(exception.Issues);
        }
        catch (Exception exception) when (IsFailClosedReadFailure(exception))
        {
            return RejectText(
                "legal-catalog-document-unreadable",
                componentId);
        }
    }

    private async Task<LoadedCatalog> LoadWithManifestAsync(
        CancellationToken cancellationToken)
    {
        var verification = await _verifier
            .VerifyAsync(
                _bundleDirectory,
                _verificationScope,
                cancellationToken)
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
                "legal-catalog-document-reference-invalid",
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
                "legal-catalog-document-reference-invalid",
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
        ParsedLegalCatalog parsed;
        try
        {
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            parsed = LegalCatalogV3Parser.Parse(
                bytes,
                identity.BundleId,
                manifest.Keys.ToHashSet(pathComparer),
                _bundleDirectory);
        }
        catch (InvalidDataException)
        {
            throw Reject("legal-catalog-invalid", CatalogFileName);
        }

        return new LegalCatalogSnapshot(
            parsed.BundleId,
            parsed.ProductVersion,
            identity.ManifestSha256,
            parsed.Components.Select(component =>
                new LegalCatalogComponent(
                    component.Id,
                    component.DisplayName,
                    component.Version,
                    component.LicenseExpression,
                    component.Usage,
                    component.Linkage,
                    component.Modified,
                    component.SourceInformation,
                    component.CopyrightNotice,
                    component.LegalDocuments.Select(reference =>
                        new LegalDocumentReference(
                            MapKind(reference.Kind),
                            reference.Path)).ToArray()))
                .ToArray());
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

    private static LegalDocumentKind MapKind(LegalFileKind kind) => kind switch
    {
        LegalFileKind.License => LegalDocumentKind.License,
        LegalFileKind.Notice => LegalDocumentKind.Notice,
        LegalFileKind.Copyright => LegalDocumentKind.Copyright,
        LegalFileKind.Attribution => LegalDocumentKind.Attribution,
        LegalFileKind.AssetManifest => LegalDocumentKind.AssetManifest,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ReferenceSubject(
        string componentId,
        LegalDocumentReference reference) =>
        $"{componentId}:{reference.Kind}:{reference.RelativePath}";

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
