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
            Request(first.StagingPath, firstPackage, first.Registrations),
            CancellationToken.None);
        var secondResult = await orchestrator.GenerateAsync(
            Request(second.StagingPath, secondPackage, second.Registrations),
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
            payload.Registrations) with
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
                    payload.Registrations),
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
    public async Task MissingMaterialSymbolsReleaseEvidenceFailsClosed()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var packagePath = Path.Combine(directory.Path, "release.zip");
        var request = Request(
            payload.StagingPath,
            packagePath,
            payload.Registrations) with
        {
            MaterialSymbolsEvidence = null,
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        AssertRejected(
            result,
            packagePath,
            "material-symbols-release-evidence-missing");
    }

    [Theory]
    [InlineData("source", "material-symbols-source-hash-mismatch")]
    [InlineData("output", "material-symbols-output-hash-mismatch")]
    public async Task RepositoryAssetHashMismatchFailsClosed(
        string asset,
        string expectedIssueCode)
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var path = asset == "source"
            ? MaterialSymbolsManifestTestFixture.SourcePath
            : MaterialSymbolsManifestTestFixture.OutputPath;
        await File.WriteAllTextAsync(
            Path.Combine(
                payload.Evidence.RepositoryRoot,
                path.Replace('/', Path.DirectorySeparatorChar)),
            "tampered\n");
        var packagePath = Path.Combine(directory.Path, "release.zip");

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(
                Request(
                    payload.StagingPath,
                    packagePath,
                    payload.Registrations),
                CancellationToken.None);

        AssertRejected(result, packagePath, expectedIssueCode);
    }

    [Theory]
    [InlineData("component", "not-material-symbols")]
    [InlineData("upstream", "https://example.invalid/icons")]
    [InlineData("commit", "ffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("license", "MIT")]
    [InlineData("path-glob", "assets/**/*")]
    [InlineData("manifest", "ui/other-icons.yml")]
    [InlineData("redistribution", "false")]
    [InlineData("trademark", "true")]
    [InlineData("product-logo", "true")]
    [InlineData("runtime-network", "true")]
    [InlineData("approval", "")]
    [InlineData("approval", "../RIGHTS-APPROVAL")]
    [InlineData("evidence", "")]
    [InlineData("evidence", "../material-symbols.md")]
    public async Task RightsLedgerMismatchFailsClosed(
        string mutation,
        string value)
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var entry = payload.Evidence.RightsLedgerEntry;
        entry = mutation switch
        {
            "component" => entry with { ComponentRef = value },
            "upstream" => entry with { Upstream = value },
            "commit" => entry with { Commit = value },
            "license" => entry with { License = value },
            "path-glob" => entry with { PathGlob = value },
            "manifest" => entry with { SelectedAssetManifest = value },
            "redistribution" => entry with
            {
                RedistributionApproved = bool.Parse(value),
            },
            "trademark" => entry with { TrademarkUse = bool.Parse(value) },
            "product-logo" => entry with
            {
                ProductLogoUse = bool.Parse(value),
            },
            "runtime-network" => entry with
            {
                RuntimeNetworkAllowed = bool.Parse(value),
            },
            "approval" => entry with { ApprovalId = value },
            "evidence" => entry with { Evidence = value },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        var request = Request(
            payload.StagingPath,
            Path.Combine(directory.Path, "release.zip"),
            payload.Registrations) with
        {
            MaterialSymbolsEvidence = payload.Evidence with
            {
                RightsLedgerEntry = entry,
            },
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        AssertRejected(
            result,
            request.PackagePath,
            "material-symbols-rights-ledger-mismatch");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("hash")]
    [InlineData("kind")]
    [InlineData("mapping")]
    [InlineData("missing-mapping")]
    [InlineData("duplicate-mapping")]
    [InlineData("mapping-traversal")]
    [InlineData("ledger-id")]
    public async Task StagingRightsRegistrationMismatchFailsClosed(
        string mutation)
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var registrations = payload.Registrations.ToArray();
        var assetIndex = Array.FindIndex(registrations, registration =>
            registration.ComponentId == "material-symbols");
        var asset = registrations[assetIndex];
        var evidence = payload.Evidence;
        switch (mutation)
        {
            case "owner":
                registrations[assetIndex] = asset with
                {
                    ComponentId = "unrelated-component",
                };
                break;
            case "hash":
                registrations[assetIndex] = asset with
                {
                    Sha256 = new string('f', 64),
                };
                await File.WriteAllTextAsync(
                    Path.Combine(
                        payload.StagingPath,
                        MaterialSymbolsManifestTestFixture.StagingPath
                            .Replace('/', Path.DirectorySeparatorChar)),
                    "different registered asset\n");
                break;
            case "kind":
                registrations[assetIndex] = asset with
                {
                    Kind = StagedArtifactKind.Executable,
                };
                break;
            case "mapping":
                evidence = evidence with
                {
                    StagedAssets =
                    [
                        evidence.StagedAssets[0] with
                        {
                            OutputPath =
                                "src/VRRecorder.DesignSystem/Assets/" +
                                "MaterialSymbols/unlisted.svg",
                        },
                    ],
                };
                break;
            case "missing-mapping":
                evidence = evidence with { StagedAssets = [] };
                break;
            case "duplicate-mapping":
                evidence = evidence with
                {
                    StagedAssets =
                    [
                        evidence.StagedAssets[0],
                        evidence.StagedAssets[0],
                    ],
                };
                break;
            case "mapping-traversal":
                evidence = evidence with
                {
                    StagedAssets =
                    [
                        evidence.StagedAssets[0] with
                        {
                            StagingRelativePath = "../recording-start.svg",
                        },
                    ],
                };
                break;
            case "ledger-id":
                evidence = evidence with
                {
                    StagedAssets =
                    [
                        evidence.StagedAssets[0] with
                        {
                            RightsLedgerEntryId = "unapproved-ledger-entry",
                        },
                    ],
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var packagePath = Path.Combine(directory.Path, "release.zip");
        var request = Request(
            payload.StagingPath,
            packagePath,
            registrations) with
        {
            MaterialSymbolsEvidence = evidence,
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        AssertRejected(
            result,
            packagePath,
            "material-symbols-staging-registration-mismatch");
    }

    [Theory]
    [InlineData("ui/material-symbols.yml")]
    [InlineData("docs/legal-review/assets/material-symbols.md")]
    public async Task MissingRightsLedgerEvidenceFileFailsClosed(
        string relativePath)
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        File.Delete(Path.Combine(
            payload.Evidence.RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var packagePath = Path.Combine(directory.Path, "release.zip");

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(
                Request(
                    payload.StagingPath,
                    packagePath,
                    payload.Registrations),
                CancellationToken.None);

        AssertRejected(
            result,
            packagePath,
            "material-symbols-rights-ledger-mismatch");
    }

    [Theory]
    [InlineData("source", "material-symbols-asset-link-not-allowed")]
    [InlineData("output", "material-symbols-asset-link-not-allowed")]
    [InlineData("manifest", "material-symbols-rights-ledger-mismatch")]
    [InlineData("evidence", "material-symbols-rights-ledger-mismatch")]
    public async Task LinkedRepositoryEvidenceFailsClosed(
        string kind,
        string expectedIssueCode)
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var relativePath = kind switch
        {
            "source" => MaterialSymbolsManifestTestFixture.SourcePath,
            "output" => MaterialSymbolsManifestTestFixture.OutputPath,
            "manifest" => "ui/material-symbols.yml",
            "evidence" => "docs/legal-review/assets/material-symbols.md",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var path = Path.Combine(
            payload.Evidence.RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var target = Path.Combine(
            directory.Path,
            $"outside-{kind}{Path.GetExtension(path)}");
        File.Move(path, target);
        File.CreateSymbolicLink(path, target);
        var packagePath = Path.Combine(directory.Path, "release.zip");

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(
                Request(
                    payload.StagingPath,
                    packagePath,
                    payload.Registrations),
                CancellationToken.None);

        AssertRejected(result, packagePath, expectedIssueCode);
    }

    [Fact]
    public async Task RepositoryRootWithTraversalSegmentsFailsClosed()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var child = Path.Combine(payload.Evidence.RepositoryRoot, "child");
        Directory.CreateDirectory(child);
        var request = Request(
            payload.StagingPath,
            Path.Combine(directory.Path, "release.zip"),
            payload.Registrations) with
        {
            MaterialSymbolsEvidence = payload.Evidence with
            {
                RepositoryRoot = Path.Combine(child, ".."),
            },
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        AssertRejected(
            result,
            request.PackagePath,
            "material-symbols-release-evidence-invalid");
    }

    [Fact]
    public async Task RepositoryRootWithLinkedAncestorFailsClosed()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var actualParent = Path.Combine(directory.Path, "actual-parent");
        Directory.CreateDirectory(actualParent);
        var movedRepository = Path.Combine(actualParent, "repository");
        Directory.Move(payload.Evidence.RepositoryRoot, movedRepository);
        var linkedParent = Path.Combine(directory.Path, "linked-parent");
        Directory.CreateSymbolicLink(linkedParent, actualParent);
        var request = Request(
            payload.StagingPath,
            Path.Combine(directory.Path, "release.zip"),
            payload.Registrations) with
        {
            MaterialSymbolsEvidence = payload.Evidence with
            {
                RepositoryRoot = Path.Combine(linkedParent, "repository"),
            },
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        AssertRejected(
            result,
            request.PackagePath,
            "material-symbols-release-evidence-invalid");
    }

    [Fact]
    public async Task MatchingUnsafeApprovalIdsStillFailClosed()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        const string unsafeApprovalId = "../RIGHTS-APPROVAL";
        var graph = Graph();
        graph = graph with
        {
            Components = graph.Components.Select(component =>
                component.Id == "material-symbols"
                    ? component with
                    {
                        Approval = component.Approval with
                        {
                            TicketId = unsafeApprovalId,
                        },
                    }
                    : component).ToArray(),
        };
        var request = Request(
            payload.StagingPath,
            Path.Combine(directory.Path, "release.zip"),
            payload.Registrations) with
        {
            ComponentGraph = graph,
            MaterialSymbolsEvidence = payload.Evidence with
            {
                RightsLedgerEntry = payload.Evidence.RightsLedgerEntry with
                {
                    ApprovalId = unsafeApprovalId,
                },
            },
        };

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(request, CancellationToken.None);

        AssertRejected(
            result,
            request.PackagePath,
            "material-symbols-rights-ledger-mismatch");
    }

    [Fact]
    public async Task StagedMaterialAssetByteMismatchUsesFinalInventoryGate()
    {
        using var directory = TemporaryDirectory.Create();
        var payload = await StagePayloadAsync(directory.Path, "staging");
        var stagedAsset = Path.Combine(
            payload.StagingPath,
            MaterialSymbolsManifestTestFixture.StagingPath
                .Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(stagedAsset, "tampered after approval\n");
        var packagePath = Path.Combine(directory.Path, "release.zip");

        var result = await new LegalReleasePackageOrchestrator()
            .GenerateAsync(
                Request(
                    payload.StagingPath,
                    packagePath,
                    payload.Registrations),
                CancellationToken.None);

        AssertRejected(result, packagePath, "staging-file-hash-mismatch");
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
            payload.Registrations) with
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
            payload.Registrations) with
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
        IReadOnlyList<RegisteredStagedArtifact> registrations)
    {
        var evidenceRoot = Path.Combine(
            Path.GetDirectoryName(stagingPath)!,
            $"{Path.GetFileName(stagingPath)}-repository");
        var evidence = CreateEvidence(evidenceRoot);
        return new ReleaseLegalPackageRequest(
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
            registrations)
        {
            MaterialSymbolsEvidence = evidence,
        };
    }

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
        var materialAssetPath = Path.Combine(
            stagingPath,
            MaterialSymbolsManifestTestFixture.StagingPath
                .Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(materialAssetPath)!);
        await File.WriteAllTextAsync(
            materialAssetPath,
            MaterialSymbolsManifestTestFixture.OutputContent);
        var evidenceRoot = Path.Combine(root, $"{stagingName}-repository");
        await WriteRepositoryEvidenceAsync(evidenceRoot);
        return new StagedPayload(
            stagingPath,
            [
                new RegisteredStagedArtifact(
                    "vr-recorder",
                    relativePath,
                    Hash(content),
                    StagedArtifactKind.Executable),
                new RegisteredStagedArtifact(
                    "material-symbols",
                    MaterialSymbolsManifestTestFixture.StagingPath,
                    MaterialSymbolsManifestTestFixture.OutputSha256,
                    StagedArtifactKind.Asset),
            ],
            CreateEvidence(evidenceRoot));
    }

    private static MaterialSymbolsReleaseEvidence CreateEvidence(
        string repositoryRoot) =>
        new(
            repositoryRoot,
            new MaterialSymbolsRightsLedgerEntry(
                "material-symbols-ui-icons",
                "src/VRRecorder.DesignSystem/Assets/MaterialSymbols/**/*",
                "material-symbols",
                "https://github.com/google/material-design-icons",
                MaterialSymbolsManifestTestFixture.Commit,
                "ui/material-symbols.yml",
                "Apache-2.0",
                "docs/legal-review/assets/material-symbols.md",
                TrademarkUse: false,
                ProductLogoUse: false,
                RuntimeNetworkAllowed: false,
                RedistributionApproved: true,
                ApprovalId: "TEST-RIGHTS-MATERIAL-SYMBOLS"),
            [
                new MaterialSymbolsStagedAssetRegistration(
                    MaterialSymbolsManifestTestFixture.OutputPath,
                    MaterialSymbolsManifestTestFixture.StagingPath,
                    "material-symbols-ui-icons"),
            ]);

    private static async Task WriteRepositoryEvidenceAsync(string root)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MaterialSymbolsManifestTestFixture.SourcePath] =
                MaterialSymbolsManifestTestFixture.SourceContent,
            [MaterialSymbolsManifestTestFixture.OutputPath] =
                MaterialSymbolsManifestTestFixture.OutputContent,
            ["ui/material-symbols.yml"] =
                "synthetic: material-symbols allowlist fixture\n",
            ["docs/legal-review/assets/material-symbols.md"] =
                "Synthetic independent rights review fixture.\n",
        };
        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }
    }

    private static void AssertRejected(
        LegalReleasePackageResult result,
        string packagePath,
        string expectedIssueCode)
    {
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue =>
            issue.Code == expectedIssueCode);
        Assert.Null(result.AuthenticatedAnchor);
        Assert.Null(result.LegalBundleRelativePath);
        Assert.False(File.Exists(packagePath));
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
        IReadOnlyList<RegisteredStagedArtifact> Registrations,
        MaterialSymbolsReleaseEvidence Evidence);

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
