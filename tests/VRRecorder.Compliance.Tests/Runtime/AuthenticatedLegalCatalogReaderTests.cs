using System.Security.Cryptography;
using System.Text;
using VRRecorder.Application.Compliance;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Runtime;

namespace VRRecorder.Compliance.Tests.Runtime;

public sealed class AuthenticatedLegalCatalogReaderTests
{
    [Fact]
    public async Task ListsGeneratedComponentsDeterministicallyAndReadsExactLicenseText()
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await WriteGeneratedBundleAsync(directory.Path);
        var reader = CreateReader(directory.Path, fixture.Anchor);

        var catalogResult = await reader.ReadAsync(CancellationToken.None);
        var catalog = Assert.IsType<LegalCatalogReadResult.Available>(
            catalogResult).Catalog;

        Assert.Equal(fixture.BundleId, catalog.BundleId);
        Assert.Equal("0.1.0", catalog.ProductVersion);
        Assert.Equal(["a", "b"], catalog.Components.Select(item => item.Id));
        var first = catalog.Components[0];
        Assert.Equal("Component a", first.DisplayName);
        Assert.Equal("1.0.0", first.Version);
        Assert.Equal("MIT", first.LicenseExpression);
        Assert.Equal("runtime-feature", first.Usage);
        Assert.Equal("managed-library", first.Linkage);
        Assert.False(first.Modified);
        Assert.Equal("offline source a@commit", first.SourceInformation);
        Assert.Equal("LICENSES/a/LICENSE.txt", first.LicenseTextPath);

        var textResult = await reader.ReadLicenseTextAsync(
            "a",
            CancellationToken.None);
        var document = Assert.IsType<LegalTextReadResult.Available>(
            textResult).Document;

        Assert.Equal("a", document.ComponentId);
        Assert.Equal("LICENSES/a/LICENSE.txt", document.RelativePath);
        Assert.Equal("a LICENSE\nline two\nline three\n", document.Text);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("LICENSES\\a\\LICENSE.txt")]
    [InlineData("THIRD-PARTY-NOTICES.txt")]
    public async Task RejectsLicenseReferenceThatIsNotAListedLicenseArtifact(
        string licenseTextPath)
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await WriteGeneratedBundleAsync(directory.Path);
        var catalogPath = Path.Combine(
            directory.Path,
            "THIRD-PARTY-COMPONENTS.json");
        var catalog = await File.ReadAllTextAsync(catalogPath);
        catalog = catalog.Replace(
            "LICENSES/a/LICENSE.txt",
            licenseTextPath,
            StringComparison.Ordinal);
        var anchor = await RewriteCatalogAndManifestAsync(
            directory.Path,
            fixture.BundleId,
            catalog);
        var reader = CreateReader(directory.Path, anchor);

        var result = await reader.ReadAsync(CancellationToken.None);

        var rejected = Assert.IsType<LegalCatalogReadResult.Rejected>(result);
        Assert.Contains(rejected.Issues, issue =>
            issue.Code == "legal-catalog-license-reference-invalid");
    }

    [Fact]
    public async Task RejectsAuthenticatedCatalogWithDuplicateComponentProperties()
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await WriteGeneratedBundleAsync(directory.Path);
        var catalogPath = Path.Combine(
            directory.Path,
            "THIRD-PARTY-COMPONENTS.json");
        var catalog = await File.ReadAllTextAsync(catalogPath);
        catalog = catalog.Replace(
            "\"displayName\": \"Component a\",",
            "\"displayName\": \"Component a\",\n      \"displayName\": \"Impostor\",",
            StringComparison.Ordinal);
        var anchor = await RewriteCatalogAndManifestAsync(
            directory.Path,
            fixture.BundleId,
            catalog);
        var reader = CreateReader(directory.Path, anchor);

        var result = await reader.ReadAsync(CancellationToken.None);

        var rejected = Assert.IsType<LegalCatalogReadResult.Rejected>(result);
        Assert.Contains(rejected.Issues, issue =>
            issue.Code == "legal-bundle-catalog-invalid" ||
            issue.Code == "legal-catalog-invalid");
    }

    [Fact]
    public async Task TamperAfterCatalogReadRejectsLicenseWithoutReturningStaleText()
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await WriteGeneratedBundleAsync(directory.Path);
        var reader = CreateReader(directory.Path, fixture.Anchor);
        Assert.IsType<LegalCatalogReadResult.Available>(
            await reader.ReadAsync(CancellationToken.None));
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "LICENSES", "a", "LICENSE.txt"),
            "tampered text");

        var result = await reader.ReadLicenseTextAsync(
            "a",
            CancellationToken.None);

        var rejected = Assert.IsType<LegalTextReadResult.Rejected>(result);
        Assert.Contains(rejected.Issues, issue =>
            issue.Code == "legal-bundle-payload-hash-mismatch" &&
            issue.Subject == "LICENSES/a/LICENSE.txt");
    }

    [Fact]
    public async Task RejectsManifestCoveredLicenseThatTraversesASymbolicLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        using var outside = TemporaryDirectory.Create();
        var fixture = await WriteGeneratedBundleAsync(directory.Path);
        var licenseDirectory = Path.Combine(directory.Path, "LICENSES", "a");
        Directory.Delete(licenseDirectory, recursive: true);
        await File.WriteAllTextAsync(
            Path.Combine(outside.Path, "LICENSE.txt"),
            "a LICENSE\nline two\nline three\n");
        Directory.CreateSymbolicLink(licenseDirectory, outside.Path);
        var reader = CreateReader(directory.Path, fixture.Anchor);

        var result = await reader.ReadLicenseTextAsync(
            "a",
            CancellationToken.None);

        var rejected = Assert.IsType<LegalTextReadResult.Rejected>(result);
        Assert.Contains(rejected.Issues, issue =>
            issue.Code == "legal-bundle-reparse-point");
    }

    private static AuthenticatedLegalCatalogReader CreateReader(
        string directory,
        AuthenticatedLegalBundleAnchor anchor) =>
        new(
            directory,
            new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(anchor)));

    private static async Task<BundleFixture> WriteGeneratedBundleAsync(
        string directory)
    {
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace:
                "https://example.invalid/spdx/wrist-legal-reader",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");
        var graph = new NormalizedComponentGraph(
            Dependencies:
            [
                new NuGetPackage(
                    "Package.B",
                    "2.0.0",
                    NuGetDependencyKind.Transitive),
                new NuGetPackage(
                    "Package.A",
                    "1.0.0",
                    NuGetDependencyKind.Direct),
            ],
            Components:
            [
                Component("b", "Package.B", "2.0.0", modified: true),
                Component("a", "Package.A", "1.0.0", modified: false),
            ]);
        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.True(eligibility.IsApproved);
        var artifacts = LegalArtifactSetGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory,
            artifacts,
            CancellationToken.None);
        var manifest = artifacts.Artifacts.Single(artifact =>
            artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        return new BundleFixture(
            context.DocumentNamespace,
            new AuthenticatedLegalBundleAnchor(
                context.DocumentNamespace,
                manifest.Sha256));
    }

    private static NormalizedComponent Component(
        string id,
        string packageId,
        string version,
        bool modified)
    {
        var legalText = $"{id} LICENSE\nline two\nline three\n";
        return new NormalizedComponent(
            Id: id,
            DisplayName: $"Component {id}",
            Version: version,
            License: new LicenseDecision("MIT", "MIT"),
            CopyrightNotice: $"Copyright Component {id}",
            Usage: "runtime-feature",
            Linkage: "managed-library",
            Modified: modified,
            SourceInformation: $"offline source {id}@commit",
            LicenseText: legalText,
            LegalFiles:
            [
                new VerifiedLegalFile(
                    LegalFileKind.License,
                    $"LICENSES/{id}/LICENSE.txt",
                    Hash(Encoding.UTF8.GetBytes(legalText)),
                    legalText),
            ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: $"LEGAL-{id}",
                RequestedBy: "developer",
                Reviewer: "legal-reviewer"),
            Packages: [new NoticePackage(packageId, version)]);
    }

    private static async Task<AuthenticatedLegalBundleAnchor>
        RewriteCatalogAndManifestAsync(
            string directory,
            string bundleId,
            string catalog)
    {
        await File.WriteAllTextAsync(
            Path.Combine(directory, "THIRD-PARTY-COMPONENTS.json"),
            catalog,
            new UTF8Encoding(false, true));
        var payloads = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "LEGAL-MANIFEST.sha256")
            .Select(path => new
            {
                Path = Path
                    .GetRelativePath(directory, path)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                Content = File.ReadAllBytes(path),
            })
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        var manifest = Encoding.UTF8.GetBytes(string.Concat(payloads.Select(
            file => $"{Hash(file.Content)}  {file.Path}\n")));
        await File.WriteAllBytesAsync(
            Path.Combine(directory, "LEGAL-MANIFEST.sha256"),
            manifest);
        return new AuthenticatedLegalBundleAnchor(bundleId, Hash(manifest));
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed record BundleFixture(
        string BundleId,
        AuthenticatedLegalBundleAnchor Anchor);

    private sealed class FixedAuthenticatedAnchorSource(
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
                $"vr-recorder-legal-catalog-{Guid.NewGuid():N}");
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
