using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class MaterialSymbolsManifestAdmissionGateTests
{
    [Fact]
    public void CanonicalV2ManifestProducesApprovedGraph()
    {
        var graph = Graph(MaterialComponent(ManifestText()));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.True(result.IsApproved);
        Assert.Same(graph, result.ApprovedGraph?.Graph);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("schema-version", "material-symbols-manifest-invalid")]
    [InlineData("unknown-member", "material-symbols-manifest-invalid")]
    [InlineData("legacy-icons", "material-symbols-manifest-invalid")]
    [InlineData("empty-icons", "material-symbols-manifest-invalid")]
    [InlineData("placeholder-commit", "material-symbols-placeholder-value")]
    [InlineData("placeholder-source-hash", "material-symbols-placeholder-value")]
    [InlineData("placeholder-output-hash", "material-symbols-placeholder-value")]
    [InlineData("placeholder-tool", "material-symbols-placeholder-value")]
    [InlineData("placeholder-recipe", "material-symbols-placeholder-value")]
    [InlineData("repository", "material-symbols-source-policy-mismatch")]
    [InlineData("license", "material-symbols-source-policy-mismatch")]
    [InlineData("acquisition", "material-symbols-source-policy-mismatch")]
    [InlineData("runtime-network", "material-symbols-source-policy-mismatch")]
    [InlineData("duplicate-semantic-id", "material-symbols-icon-duplicate")]
    [InlineData("duplicate-output-path", "material-symbols-icon-duplicate")]
    [InlineData("invalid-source-hash", "material-symbols-icon-hash-invalid")]
    [InlineData("invalid-output-hash", "material-symbols-icon-hash-invalid")]
    [InlineData(
        "modified-without-notice",
        "material-symbols-modification-notice-missing")]
    [InlineData(
        "interactive-without-name",
        "material-symbols-accessibility-metadata-missing")]
    [InlineData(
        "interactive-without-tooltip",
        "material-symbols-accessibility-metadata-missing")]
    [InlineData("outside-output-root", "material-symbols-output-path-invalid")]
    public void InvalidManifestCannotProduceApprovedGraph(
        string mutation,
        string expectedIssueCode)
    {
        var manifest = Manifest();
        Mutate(manifest, mutation);
        var graph = Graph(MaterialComponent(ManifestText(manifest)));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == expectedIssueCode &&
            issue.Subject.StartsWith(
                "material-symbols",
                StringComparison.Ordinal));
    }

    [Fact]
    public void MaterialComponentWithoutManifestFailsClosed()
    {
        var component = MaterialComponent(ManifestText()) with
        {
            LegalFiles = MaterialComponent(ManifestText()).LegalFiles
                .Where(file => file.Kind != LegalFileKind.AssetManifest)
                .ToArray(),
        };

        var result = ReleaseEligibilityGate.Evaluate(Graph(component));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "material-symbols-manifest-missing" &&
            issue.Subject == "material-symbols");
    }

    [Fact]
    public void MaterialComponentWithMultipleManifestsFailsClosed()
    {
        var component = MaterialComponent(ManifestText());
        component = component with
        {
            LegalFiles =
            [
                .. component.LegalFiles,
                component.LegalFiles.Single(file =>
                    file.Kind == LegalFileKind.AssetManifest),
            ],
        };

        var result = ReleaseEligibilityGate.Evaluate(Graph(component));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "material-symbols-manifest-count-invalid" &&
            issue.Subject == "material-symbols");
    }

    [Fact]
    public void NonMaterialComponentCannotOwnAssetManifest()
    {
        var component = MaterialComponent(ManifestText()) with
        {
            Id = "not-material-symbols",
        };

        var result = ReleaseEligibilityGate.Evaluate(Graph(component));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "material-symbols-manifest-owner-invalid" &&
            issue.Subject == "not-material-symbols");
    }

    [Fact]
    public void MaterialComponentMustUseApache20()
    {
        var component = MaterialComponent(ManifestText()) with
        {
            License = new LicenseDecision("MIT", "MIT"),
        };

        var result = ReleaseEligibilityGate.Evaluate(Graph(component));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "material-symbols-license-policy-mismatch" &&
            issue.Subject == "material-symbols");
    }

    [Theory]
    [InlineData(
        LegalApprovalStatus.Pending,
        "pending-independent-review")]
    [InlineData(LegalApprovalStatus.Rejected, "component-not-approved")]
    public void MaterialComponentRequiresIndependentApproval(
        LegalApprovalStatus status,
        string expectedIssueCode)
    {
        var component = MaterialComponent(ManifestText()) with
        {
            Approval = new LegalApproval(
                status,
                TicketId: null,
                RequestedBy: "asset-importer",
                Reviewer: null),
        };

        var result = ReleaseEligibilityGate.Evaluate(Graph(component));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == expectedIssueCode &&
            issue.Subject == "material-symbols");
    }

    private static NormalizedComponentGraph Graph(
        params NormalizedComponent[] components) =>
        new(Dependencies: [], Components: components);

    private static NormalizedComponent MaterialComponent(string manifest)
    {
        const string license = "Apache License 2.0 test fixture\n";
        return new NormalizedComponent(
            Id: "material-symbols",
            DisplayName: "Material Symbols (Material Design icons by Google)",
            Version: "0123456789abcdef0123456789abcdef01234567",
            License: new LicenseDecision("Apache-2.0", "Apache-2.0"),
            CopyrightNotice: "Copyright Google LLC",
            Usage: "user-interface-icons",
            Linkage: "runtime-bundled-assets",
            Modified: true,
            SourceInformation:
                "https://github.com/google/material-design-icons@" +
                "0123456789abcdef0123456789abcdef01234567",
            LicenseText: license,
            LegalFiles:
            [
                LegalFile(
                    LegalFileKind.License,
                    "LICENSES/material-symbols/LICENSE.txt",
                    license),
                LegalFile(
                    LegalFileKind.AssetManifest,
                    "MATERIAL-SYMBOLS-MANIFEST.json",
                    manifest),
            ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: "TEST-RIGHTS-MATERIAL-SYMBOLS",
                RequestedBy: "asset-importer",
                Reviewer: "independent-rights-reviewer"),
            Packages: []);
    }

    private static VerifiedLegalFile LegalFile(
        LegalFileKind kind,
        string path,
        string content) =>
        new(kind, path, Hash(content), content);

    private static string Hash(string content) => Convert
        .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
        .ToLowerInvariant();

    private static string ManifestText(JsonObject? manifest = null) =>
        (manifest ?? Manifest()).ToJsonString() + "\n";

    private static JsonObject Manifest() =>
        new()
        {
            ["schemaVersion"] = 2,
            ["componentId"] = "material-symbols",
            ["displayName"] =
                "Material Symbols (Material Design icons by Google)",
            ["source"] = new JsonObject
            {
                ["repository"] =
                    "https://github.com/google/material-design-icons",
                ["browser"] = "https://fonts.google.com/icons",
                ["commit"] =
                    "0123456789abcdef0123456789abcdef01234567",
                ["codepointsFile"] =
                    "upstream/MaterialSymbolsRounded.codepoints",
                ["licenseExpression"] = "Apache-2.0",
                ["licenseFile"] =
                    "LICENSES/material-symbols/LICENSE.txt",
                ["attributionFile"] =
                    "RIGHTS/material-symbols-attribution.txt",
                ["noticeStatus"] = "absent",
                ["acquisitionMode"] = "approved-update-pr-only",
                ["runtimeNetworkAllowed"] = false,
            },
            ["rightsPolicy"] = new JsonObject
            {
                ["permittedAssetClass"] =
                    "general-user-interface-symbol",
                ["attributionDisplayedInLegalUi"] = true,
                ["licenseTextBundledOffline"] = true,
                ["sourceAndOutputHashesRequired"] = true,
                ["modificationRecordRequired"] = true,
                ["exclusiveTrademarkUseAllowed"] = false,
                ["implyGoogleAffiliationAllowed"] = false,
                ["prohibitedAssetClasses"] = new JsonArray(
                    "google-logo",
                    "google-g",
                    "google-product-icon",
                    "third-party-logo",
                    "trademark-or-brand-mark",
                    "unregistered-symbol",
                    "runtime-cdn-font"),
            },
            ["rendering"] = new JsonObject
            {
                ["family"] = "Material Symbols Rounded",
                ["sourceFormat"] = "svg",
                ["outputFormat"] = "optimized-svg",
                ["defaultAxes"] = Axes(fill: 0, opticalSize: 24),
                ["primaryActionOpticalSize"] = 48,
                ["conversionTool"] = "svgo@3.3.2",
                ["conversionRecipe"] =
                    "build/material-symbols/svgo.config.mjs",
                ["deterministicOutputRequired"] = true,
                ["externalFontFileAtRuntime"] = false,
            },
            ["validation"] = new JsonObject
            {
                ["failOnUnknownUpstreamName"] = true,
                ["failOnCodepointMismatch"] = true,
                ["failOnUnregisteredOutputAsset"] = true,
                ["failOnMissingAccessibleNameForInteractiveIcon"] = true,
                ["failOnMissingTooltipForAmbiguousIconOnlyControl"] = true,
                ["failOnIncorrectRtlMirroring"] = true,
                ["failOnSourceOrOutputHashMismatch"] = true,
                ["failOnLicenseOrAttributionMismatch"] = true,
            },
            ["selectedIcons"] = new JsonArray(Icon()),
        };

    private static JsonObject Icon() =>
        new()
        {
            ["semanticId"] = "recording.start",
            ["upstreamName"] = "fiber_manual_record",
            ["codepoint"] = "e061",
            ["style"] = "rounded",
            ["axes"] = Axes(fill: 0, opticalSize: 48),
            ["rtlMirror"] = false,
            ["interactive"] = true,
            ["sourcePath"] =
                "upstream/symbols/fiber_manual_record.svg",
            ["sourceSha256"] = new string('a', 64),
            ["outputPath"] =
                "src/VRRecorder.DesignSystem/Assets/MaterialSymbols/" +
                "recording-start.svg",
            ["outputSha256"] = new string('b', 64),
            ["modified"] = true,
            ["modificationNotice"] =
                "Optimized deterministically with svgo@3.3.2.",
            ["visibleLabelKey"] = "Recording_Start_Short",
            ["accessibleNameKey"] = "Recording_Start_AccessibleName",
            ["tooltipKey"] = "Recording_Start_Tooltip",
            ["surfaces"] = new JsonArray("wrist", "desktop"),
        };

    private static JsonObject Axes(int fill, int opticalSize) =>
        new()
        {
            ["fill"] = fill,
            ["weight"] = 500,
            ["grade"] = 0,
            ["opticalSize"] = opticalSize,
        };

    private static void Mutate(JsonObject manifest, string mutation)
    {
        var source = manifest["source"]!.AsObject();
        var rendering = manifest["rendering"]!.AsObject();
        var icons = manifest["selectedIcons"]!.AsArray();
        var first = icons[0]!.AsObject();
        switch (mutation)
        {
            case "schema-version":
                manifest["schemaVersion"] = 1;
                break;
            case "unknown-member":
                manifest["unexpected"] = true;
                break;
            case "legacy-icons":
                manifest["icons"] = icons.DeepClone();
                manifest.Remove("selectedIcons");
                break;
            case "empty-icons":
                manifest["selectedIcons"] = new JsonArray();
                break;
            case "placeholder-commit":
                source["commit"] = "<full-commit>";
                break;
            case "placeholder-source-hash":
                first["sourceSha256"] = "<sha256>";
                break;
            case "placeholder-output-hash":
                first["outputSha256"] = "<sha256>";
                break;
            case "placeholder-tool":
                rendering["conversionTool"] = "<tool-and-version>";
                break;
            case "placeholder-recipe":
                rendering["conversionRecipe"] = "<recipe>";
                break;
            case "repository":
                source["repository"] = "https://example.invalid/icons";
                break;
            case "license":
                source["licenseExpression"] = "MIT";
                break;
            case "acquisition":
                source["acquisitionMode"] = "runtime-download";
                break;
            case "runtime-network":
                source["runtimeNetworkAllowed"] = true;
                break;
            case "duplicate-semantic-id":
            {
                var duplicate = Icon();
                duplicate["outputPath"] =
                    "src/VRRecorder.DesignSystem/Assets/MaterialSymbols/" +
                    "recording-start-copy.svg";
                icons.Add(duplicate);
                break;
            }
            case "duplicate-output-path":
            {
                var duplicate = Icon();
                duplicate["semanticId"] = "recording.stop";
                duplicate["outputPath"] =
                    "SRC/VRRecorder.DesignSystem/Assets/MaterialSymbols/" +
                    "RECORDING-START.SVG";
                icons.Add(duplicate);
                break;
            }
            case "invalid-source-hash":
                first["sourceSha256"] = "not-a-hash";
                break;
            case "invalid-output-hash":
                first["outputSha256"] = "not-a-hash";
                break;
            case "modified-without-notice":
                first.Remove("modificationNotice");
                break;
            case "interactive-without-name":
                first.Remove("accessibleNameKey");
                break;
            case "interactive-without-tooltip":
                first.Remove("tooltipKey");
                break;
            case "outside-output-root":
                first["outputPath"] = "assets/recording-start.svg";
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mutation),
                    mutation,
                    "Unknown test mutation.");
        }
    }
}
