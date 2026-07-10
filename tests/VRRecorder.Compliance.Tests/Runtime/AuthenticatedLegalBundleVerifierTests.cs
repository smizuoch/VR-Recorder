using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Domain.Recording;
using RecorderStartupUseCase =
    VRRecorder.Application.Compliance.RecorderStartupUseCase;

[assembly: AssemblyMetadata(
    "VRRecorder.LegalBundleId",
    "https://example.invalid/spdx/vr-recorder-assembly-metadata")]
[assembly: AssemblyMetadata(
    "VRRecorder.LegalManifestSha256",
    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]

namespace VRRecorder.Compliance.Tests.Runtime;

public sealed class AuthenticatedLegalBundleVerifierTests
{
    private const string BundleId =
        "https://example.invalid/spdx/vr-recorder-0.1.0";

    [Fact]
    public async Task VerifiesBundleAgainstAuthenticatedOutOfBandManifestDigest()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = Encoding.UTF8.GetBytes(CatalogJson());
        var manifest = Encoding.UTF8.GetBytes(
            $"{Hash(catalog)}  THIRD-PARTY-COMPONENTS.json\n");
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "THIRD-PARTY-COMPONENTS.json"),
            catalog);
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "LEGAL-MANIFEST.sha256"),
            manifest);
        var verifier = new AuthenticatedLegalBundleVerifier(
            new StubAuthenticatedAnchorSource(
                new AuthenticatedLegalBundleAnchor(
                    BundleId,
                    Hash(manifest))));

        var result = await verifier.VerifyAsync(
            directory.Path,
            CancellationToken.None);

        var verified = Assert.IsType<LegalBundleVerification.Verified>(result);
        Assert.Equal(BundleId, verified.Identity.BundleId);
        Assert.Equal(Hash(manifest), verified.Identity.ManifestSha256);
    }

    [Fact]
    public async Task RejectsPayloadWhoseHashDoesNotMatchAuthenticatedManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var catalogPath = Path.Combine(
            directory.Path,
            "THIRD-PARTY-COMPONENTS.json");
        var catalog = Encoding.UTF8.GetBytes(CatalogJson());
        var manifest = Encoding.UTF8.GetBytes(
            $"{Hash(catalog)}  THIRD-PARTY-COMPONENTS.json\n");
        await File.WriteAllBytesAsync(catalogPath, catalog);
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "LEGAL-MANIFEST.sha256"),
            manifest);
        await File.AppendAllTextAsync(catalogPath, "\n");
        var verifier = new AuthenticatedLegalBundleVerifier(
            new StubAuthenticatedAnchorSource(
                new AuthenticatedLegalBundleAnchor(
                    BundleId,
                    Hash(manifest))));

        var result = await verifier.VerifyAsync(
            directory.Path,
            CancellationToken.None);

        var rejected = Assert.IsType<LegalBundleVerification.Rejected>(result);
        var issue = Assert.Single(rejected.Issues);
        Assert.Equal("legal-bundle-payload-hash-mismatch", issue.Code);
        Assert.Equal("THIRD-PARTY-COMPONENTS.json", issue.Subject);
    }

    [Fact]
    public async Task RejectsPayloadFileNotListedByAuthenticatedManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = Encoding.UTF8.GetBytes(CatalogJson());
        var manifest = Encoding.UTF8.GetBytes(
            $"{Hash(catalog)}  THIRD-PARTY-COMPONENTS.json\n");
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "THIRD-PARTY-COMPONENTS.json"),
            catalog);
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "LEGAL-MANIFEST.sha256"),
            manifest);
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "UNREGISTERED.txt"),
            "not in the manifest");
        var verifier = new AuthenticatedLegalBundleVerifier(
            new StubAuthenticatedAnchorSource(
                new AuthenticatedLegalBundleAnchor(
                    BundleId,
                    Hash(manifest))));

        var result = await verifier.VerifyAsync(
            directory.Path,
            CancellationToken.None);

        var rejected = Assert.IsType<LegalBundleVerification.Rejected>(result);
        var issue = Assert.Single(rejected.Issues);
        Assert.Equal("legal-bundle-payload-unexpected", issue.Code);
        Assert.Equal("UNREGISTERED.txt", issue.Subject);
    }

    [Fact]
    [Trait("Scenario", "IT-031")]
    public async Task TamperedRuntimeBundleLocksRecorderInComplianceFault()
    {
        using var directory = TemporaryDirectory.Create();
        var catalogPath = Path.Combine(
            directory.Path,
            "THIRD-PARTY-COMPONENTS.json");
        var catalog = Encoding.UTF8.GetBytes(CatalogJson());
        var manifest = Encoding.UTF8.GetBytes(
            $"{Hash(catalog)}  THIRD-PARTY-COMPONENTS.json\n");
        await File.WriteAllBytesAsync(catalogPath, catalog);
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "LEGAL-MANIFEST.sha256"),
            manifest);
        await File.AppendAllTextAsync(catalogPath, "\n");
        var gateway = new RuntimeLegalBundleVerificationGateway(
            directory.Path,
            new AuthenticatedLegalBundleVerifier(
                new StubAuthenticatedAnchorSource(
                    new AuthenticatedLegalBundleAnchor(
                        BundleId,
                        Hash(manifest)))));

        var result = await new RecorderStartupUseCase(gateway)
            .ExecuteAsync(CancellationToken.None);

        Assert.Equal(RecorderState.ComplianceFault, result.State);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("LEGAL_BUNDLE_HASH_MISMATCH", issue.Code);
        Assert.Equal("THIRD-PARTY-COMPONENTS.json", issue.Subject);
    }

    [Fact]
    public async Task RejectsDuplicateCatalogProperties()
    {
        using var directory = TemporaryDirectory.Create();
        var catalog = Encoding.UTF8.GetBytes($$"""
            {
              "schemaVersion": 2,
              "bundleId": "{{BundleId}}",
              "bundleId": "{{BundleId}}",
              "integrityManifest": {
                "path": "LEGAL-MANIFEST.sha256",
                "algorithm": "SHA-256"
              }
            }
            """);
        var manifest = Encoding.UTF8.GetBytes(
            $"{Hash(catalog)}  THIRD-PARTY-COMPONENTS.json\n");
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "THIRD-PARTY-COMPONENTS.json"),
            catalog);
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "LEGAL-MANIFEST.sha256"),
            manifest);
        var verifier = new AuthenticatedLegalBundleVerifier(
            new StubAuthenticatedAnchorSource(
                new AuthenticatedLegalBundleAnchor(
                    BundleId,
                    Hash(manifest))));

        var result = await verifier.VerifyAsync(
            directory.Path,
            CancellationToken.None);

        var rejected = Assert.IsType<LegalBundleVerification.Rejected>(result);
        var issue = Assert.Single(rejected.Issues);
        Assert.Equal("legal-bundle-catalog-invalid", issue.Code);
        Assert.Equal("THIRD-PARTY-COMPONENTS.json", issue.Subject);
    }

    [Fact]
    public async Task MissingAuthenticatedAnchorLocksRecorderInComplianceFault()
    {
        using var directory = TemporaryDirectory.Create();
        var gateway = new RuntimeLegalBundleVerificationGateway(
            directory.Path,
            new AuthenticatedLegalBundleVerifier(
                new MissingAuthenticatedAnchorSource()));

        var result = await new RecorderStartupUseCase(gateway)
            .ExecuteAsync(CancellationToken.None);

        Assert.Equal(RecorderState.ComplianceFault, result.State);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("LEGAL_BUNDLE_MISSING", issue.Code);
        Assert.Equal("authenticated-manifest-anchor", issue.Subject);
    }

    [Fact]
    public async Task ReadsAuthenticatedAnchorFromSignedAssemblyMetadata()
    {
        var source = new AssemblyMetadataAuthenticatedLegalBundleAnchorSource(
            typeof(AuthenticatedLegalBundleVerifierTests).Assembly);

        var anchor = await source.GetAsync(CancellationToken.None);

        Assert.Equal(
            "https://example.invalid/spdx/vr-recorder-assembly-metadata",
            anchor.BundleId);
        Assert.Equal(new string('a', 64), anchor.ManifestSha256);
    }

    [Fact]
    public async Task RejectsListedPayloadThatIsASymbolicLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        using var outside = TemporaryDirectory.Create();
        var catalog = Encoding.UTF8.GetBytes(CatalogJson());
        var outsideCatalog = Path.Combine(outside.Path, "catalog.json");
        await File.WriteAllBytesAsync(outsideCatalog, catalog);
        File.CreateSymbolicLink(
            Path.Combine(directory.Path, "THIRD-PARTY-COMPONENTS.json"),
            outsideCatalog);
        var manifest = Encoding.UTF8.GetBytes(
            $"{Hash(catalog)}  THIRD-PARTY-COMPONENTS.json\n");
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "LEGAL-MANIFEST.sha256"),
            manifest);
        var verifier = new AuthenticatedLegalBundleVerifier(
            new StubAuthenticatedAnchorSource(
                new AuthenticatedLegalBundleAnchor(
                    BundleId,
                    Hash(manifest))));

        var result = await verifier.VerifyAsync(
            directory.Path,
            CancellationToken.None);

        var rejected = Assert.IsType<LegalBundleVerification.Rejected>(result);
        var issue = Assert.Single(rejected.Issues);
        Assert.Equal("legal-bundle-reparse-point", issue.Code);
        Assert.Equal("THIRD-PARTY-COMPONENTS.json", issue.Subject);
    }

    private static string CatalogJson() => $$"""
        {
          "schemaVersion": 2,
          "bundleId": "{{BundleId}}",
          "productName": "VR-Recorder",
          "productVersion": "0.1.0",
          "generatedAtUtc": "2026-07-10T00:00:00Z",
          "integrityManifest": {
            "path": "LEGAL-MANIFEST.sha256",
            "algorithm": "SHA-256"
          },
          "components": []
        }
        """;

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class StubAuthenticatedAnchorSource(
        AuthenticatedLegalBundleAnchor anchor)
        : IAuthenticatedLegalBundleAnchorSource
    {
        public ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(anchor);
        }
    }

    private sealed class MissingAuthenticatedAnchorSource
        : IAuthenticatedLegalBundleAnchorSource
    {
        public ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AuthenticatedLegalBundleAnchor>(
                new AuthenticatedLegalBundleAnchorUnavailableException(
                    "The signed application resource is missing."));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-runtime-legal-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
