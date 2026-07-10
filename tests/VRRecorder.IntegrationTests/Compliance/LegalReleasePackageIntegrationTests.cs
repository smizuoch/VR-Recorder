using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Packaging;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.IntegrationTests.Compliance;

public sealed class LegalReleasePackageIntegrationTests
{
    private static readonly string[] ExpectedPackageNames =
        ["Package.Direct", "Package.Transitive"];
    private const string BundleId =
        "https://example.invalid/spdx/vr-recorder-release-0.1.0";

    [Fact]
    [Trait("Scenario", "IT-025")]
    public async Task ApprovedGraphProducesDeterministicPackageWithAuthenticatedLegalBundle()
    {
        using var directory = TemporaryDirectory.Create();
        var first = await StagePayloadAsync(directory.Path, "staging-first");
        var second = await StagePayloadAsync(directory.Path, "staging-second");
        var firstPackage = Path.Combine(directory.Path, "first.zip");
        var secondPackage = Path.Combine(directory.Path, "second.zip");
        var orchestrator = new LegalReleasePackageOrchestrator();

        var firstResult = await orchestrator.GenerateAsync(
            Request(first.StagingPath, firstPackage, first.Registration),
            CancellationToken.None);
        var secondResult = await orchestrator.GenerateAsync(
            Request(second.StagingPath, secondPackage, second.Registration),
            CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.Empty(firstResult.Issues);
        Assert.NotNull(firstResult.AuthenticatedAnchor);
        Assert.Equal(
            "VR-Recorder-Legal/0.1.0",
            firstResult.LegalBundleRelativePath);
        Assert.True(secondResult.Succeeded);
        Assert.Equal(
            await File.ReadAllBytesAsync(firstPackage),
            await File.ReadAllBytesAsync(secondPackage));

        var extracted = Path.Combine(directory.Path, "extracted");
        ZipFile.ExtractToDirectory(firstPackage, extracted);
        var legalBundle = Path.Combine(
            extracted,
            "VR-Recorder-Legal",
            "0.1.0");
        var verification = await new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(
                    firstResult.AuthenticatedAnchor!))
            .VerifyAsync(legalBundle, CancellationToken.None);

        var verified = Assert.IsType<LegalBundleVerification.Verified>(
            verification);
        Assert.Equal(BundleId, verified.Identity.BundleId);
        Assert.Equal(
            firstResult.AuthenticatedAnchor!.ManifestSha256,
            verified.Identity.ManifestSha256);
        Assert.True(File.Exists(Path.Combine(
            extracted,
            "app",
            "VR-Recorder.exe")));

        using var sbom = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(legalBundle, "SBOM", "manifest.spdx.json")));
        var packageNames = sbom.RootElement
            .GetProperty("packages")
            .EnumerateArray()
            .Select(package => package.GetProperty("name").GetString()!)
            .ToArray();
        Assert.Equal(ExpectedPackageNames, packageNames);
    }

    [Theory]
    [Trait("Scenario", "IT-031")]
    [InlineData(
        LegalApprovalStatus.Pending,
        "pending-independent-review")]
    [InlineData(LegalApprovalStatus.Rejected, "component-not-approved")]
    public async Task UnapprovedGraphProducesNeitherAnchorNorPackage(
        LegalApprovalStatus status,
        string expectedIssue)
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var packagePath = Path.Combine(directory.Path, "release.zip");
        var graph = Graph();
        var rejectedComponent = graph.Components[0] with
        {
            Approval = graph.Components[0].Approval with { Status = status },
        };
        graph = graph with
        {
            Components = [rejectedComponent, .. graph.Components.Skip(1)],
        };
        var request = Request(
            payload.StagingPath,
            packagePath,
            payload.Registration) with
        {
            ComponentGraph = graph,
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == expectedIssue);
        Assert.Null(result.AuthenticatedAnchor);
        Assert.Null(result.LegalBundleRelativePath);
        Assert.False(File.Exists(packagePath));
        Assert.False(Directory.Exists(Path.Combine(
            payload.StagingPath,
            "VR-Recorder-Legal")));
    }

    [Fact]
    [Trait("Scenario", "IT-026")]
    public async Task RogueFinalStagingFileProducesNeitherAnchorNorPackage()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var roguePath = Path.Combine(payload.StagingPath, "native", "rogue.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(roguePath)!);
        await File.WriteAllBytesAsync(roguePath, [0x4d, 0x5a]);
        var packagePath = Path.Combine(directory.Path, "release.zip");

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(
                Request(
                    payload.StagingPath,
                    packagePath,
                    payload.Registration),
                CancellationToken.None);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Issues, item =>
            item.Code == "unregistered-staging-file");
        Assert.Equal("native/rogue.dll", issue.Subject);
        Assert.Null(result.AuthenticatedAnchor);
        Assert.Null(result.LegalBundleRelativePath);
        Assert.False(File.Exists(packagePath));
    }

    [Fact]
    public async Task MissingMaterialSymbolsManifestProducesNoReleaseArtifacts()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var packagePath = Path.Combine(directory.Path, "release.zip");
        var request = Request(
            payload.StagingPath,
            packagePath,
            payload.Registration) with
        {
            ComponentGraph = new NormalizedComponentGraph(
                Graph().Dependencies,
                Graph().Components.Where(component =>
                    component.Id != "material-symbols").ToArray()),
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "material-symbols-manifest-missing" &&
            issue.Subject == "material-symbols");
        Assert.Null(result.AuthenticatedAnchor);
        Assert.Null(result.LegalBundleRelativePath);
        Assert.False(File.Exists(packagePath));
        Assert.False(Directory.Exists(Path.Combine(
            payload.StagingPath,
            "VR-Recorder-Legal")));
    }

    [Fact]
    public async Task LegacyEmptyIconsManifestProducesNoReleaseArtifacts()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var packagePath = Path.Combine(directory.Path, "release.zip");
        const string legacyManifest =
            "{\"schemaVersion\":2,\"icons\":[]}\n";
        var graph = Graph();
        graph = graph with
        {
            Components =
            [
                .. graph.Components.Where(component =>
                    component.Id != "material-symbols"),
                MaterialSymbolsComponent(legacyManifest),
            ],
        };
        var request = Request(
            payload.StagingPath,
            packagePath,
            payload.Registration) with
        {
            ComponentGraph = graph,
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "material-symbols-manifest-invalid");
        Assert.Null(result.AuthenticatedAnchor);
        Assert.Null(result.LegalBundleRelativePath);
        Assert.False(File.Exists(packagePath));
        Assert.False(Directory.Exists(Path.Combine(
            payload.StagingPath,
            "VR-Recorder-Legal")));
    }

    private static ReleaseLegalPackageRequest Request(
        string stagingPath,
        string packagePath,
        RegisteredStagedArtifact payload) =>
        new(
            Graph(),
            new SpdxGenerationContext(
                ProductName: "VR-Recorder",
                ProductVersion: "0.1.0",
                DocumentNamespace: BundleId,
                CreatedAtUtc: new DateTimeOffset(
                    2026,
                    7,
                    10,
                    0,
                    0,
                    0,
                    TimeSpan.Zero),
                Creator: "Organization: VR-Recorder Project"),
            stagingPath,
            packagePath,
            [payload]);

    private static NormalizedComponentGraph Graph() =>
        new(
        [
            new NuGetPackage(
                "Package.Direct",
                "1.0.0",
                NuGetDependencyKind.Direct),
            new NuGetPackage(
                "Package.Transitive",
                "2.0.0",
                NuGetDependencyKind.Transitive),
        ],
        [
            Component("direct", "Package.Direct", "1.0.0"),
            Component("transitive", "Package.Transitive", "2.0.0"),
            MaterialSymbolsComponent(MaterialSymbolsManifestTestFixture.Create(
                "LICENSES/material-symbols/LICENSE.txt",
                "RIGHTS/material-symbols-attribution.txt",
                "absent")),
        ]);

    private static NormalizedComponent Component(
        string id,
        string packageId,
        string version)
    {
        var licenseText = $"{id} license text\n";
        return new NormalizedComponent(
            Id: id,
            DisplayName: $"Component {id}",
            Version: version,
            License: new LicenseDecision("MIT", "MIT"),
            CopyrightNotice: $"Copyright Component {id}",
            Usage: "runtime",
            Linkage: "managed-library",
            Modified: false,
            SourceInformation: $"https://example.invalid/{id}@commit",
            LicenseText: licenseText,
            LegalFiles:
            [
                new VerifiedLegalFile(
                    LegalFileKind.License,
                    $"LICENSES/{id}/LICENSE.txt",
                    Hash(Encoding.UTF8.GetBytes(licenseText)),
                    licenseText),
            ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: $"LEGAL-{id}",
                RequestedBy: "release-engineer",
                Reviewer: "independent-legal-reviewer"),
            Packages: [new NoticePackage(packageId, version)]);
    }

    private static NormalizedComponent MaterialSymbolsComponent(
        string manifest)
    {
        const string license = "Apache License 2.0 test fixture\n";
        return new NormalizedComponent(
            Id: "material-symbols",
            DisplayName: "Material Symbols (Material Design icons by Google)",
            Version: MaterialSymbolsManifestTestFixture.Commit,
            License: new LicenseDecision("Apache-2.0", "Apache-2.0"),
            CopyrightNotice: "Copyright Google LLC",
            Usage: "user-interface-icons",
            Linkage: "runtime-bundled-assets",
            Modified: true,
            SourceInformation:
                "https://github.com/google/material-design-icons@" +
                MaterialSymbolsManifestTestFixture.Commit,
            LicenseText: license,
            LegalFiles:
            [
                new VerifiedLegalFile(
                    LegalFileKind.License,
                    "LICENSES/material-symbols/LICENSE.txt",
                    Hash(Encoding.UTF8.GetBytes(license)),
                    license),
                new VerifiedLegalFile(
                    LegalFileKind.AssetManifest,
                    "MATERIAL-SYMBOLS-MANIFEST.json",
                    Hash(Encoding.UTF8.GetBytes(manifest)),
                    manifest),
                new VerifiedLegalFile(
                    LegalFileKind.Attribution,
                    "RIGHTS/material-symbols-attribution.txt",
                    Hash(Encoding.UTF8.GetBytes(
                        "Material Symbols test attribution\n")),
                    "Material Symbols test attribution\n"),
            ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: "TEST-RIGHTS-MATERIAL-SYMBOLS",
                RequestedBy: "asset-importer",
                Reviewer: "independent-rights-reviewer"),
            Packages: []);
    }

    private static async Task<StagedPayload> StagePayloadAsync(
        string root,
        string stagingName)
    {
        var stagingPath = Path.Combine(root, stagingName);
        var relativePath = "app/VR-Recorder.exe";
        var payloadPath = Path.Combine(stagingPath, "app", "VR-Recorder.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        byte[] content = [0x4d, 0x5a, 0x90, 0x00];
        await File.WriteAllBytesAsync(payloadPath, content);
        return new StagedPayload(
            stagingPath,
            new RegisteredStagedArtifact(
                "vr-recorder",
                relativePath,
                Hash(content),
                StagedArtifactKind.Executable));
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

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

    private sealed record StagedPayload(
        string StagingPath,
        RegisteredStagedArtifact Registration);

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
                $"vr-recorder-release-integration-{Guid.NewGuid():N}");
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
