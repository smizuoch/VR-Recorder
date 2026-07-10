using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VRRecorder.IntegrationTests.Compliance;

internal static class MaterialSymbolsManifestTestFixture
{
    public const string Commit =
        "0123456789abcdef0123456789abcdef01234567";
    public const string SourcePath =
        "upstream/symbols/fiber_manual_record.svg";
    public const string OutputPath =
        "src/VRRecorder.DesignSystem/Assets/MaterialSymbols/" +
        "recording-start.svg";
    public const string StagingPath =
        "app/Assets/MaterialSymbols/recording-start.svg";
    public const string SourceContent =
        "<svg data-fixture=\"material-symbols-source\"/>\n";
    public const string OutputContent =
        "<svg data-fixture=\"material-symbols-output\"/>\n";
    public static readonly string SourceSha256 = Hash(SourceContent);
    public static readonly string OutputSha256 = Hash(OutputContent);

    public static string Create(
        string licenseFile,
        string attributionFile,
        string noticeStatus)
    {
        var manifest = new
        {
            schemaVersion = 2,
            componentId = "material-symbols",
            displayName =
                "Material Symbols (Material Design icons by Google)",
            source = new
            {
                repository =
                    "https://github.com/google/material-design-icons",
                browser = "https://fonts.google.com/icons",
                commit = Commit,
                codepointsFile =
                    "upstream/MaterialSymbolsRounded.codepoints",
                licenseExpression = "Apache-2.0",
                licenseFile,
                attributionFile,
                noticeStatus,
                acquisitionMode = "approved-update-pr-only",
                runtimeNetworkAllowed = false,
            },
            rightsPolicy = new
            {
                permittedAssetClass = "general-user-interface-symbol",
                attributionDisplayedInLegalUi = true,
                licenseTextBundledOffline = true,
                sourceAndOutputHashesRequired = true,
                modificationRecordRequired = true,
                exclusiveTrademarkUseAllowed = false,
                implyGoogleAffiliationAllowed = false,
                prohibitedAssetClasses = new[]
                {
                    "google-logo",
                    "google-g",
                    "google-product-icon",
                    "third-party-logo",
                    "trademark-or-brand-mark",
                    "unregistered-symbol",
                    "runtime-cdn-font",
                },
            },
            rendering = new
            {
                family = "Material Symbols Rounded",
                sourceFormat = "svg",
                outputFormat = "optimized-svg",
                defaultAxes = new
                {
                    fill = 0,
                    weight = 500,
                    grade = 0,
                    opticalSize = 24,
                },
                primaryActionOpticalSize = 48,
                conversionTool = "test-svgo@1.0.0",
                conversionRecipe = "tests/fixtures/svgo.config.mjs",
                deterministicOutputRequired = true,
                externalFontFileAtRuntime = false,
            },
            validation = new
            {
                failOnUnknownUpstreamName = true,
                failOnCodepointMismatch = true,
                failOnUnregisteredOutputAsset = true,
                failOnMissingAccessibleNameForInteractiveIcon = true,
                failOnMissingTooltipForAmbiguousIconOnlyControl = true,
                failOnIncorrectRtlMirroring = true,
                failOnSourceOrOutputHashMismatch = true,
                failOnLicenseOrAttributionMismatch = true,
            },
            selectedIcons = new[]
            {
                new
                {
                    semanticId = "recording.start",
                    upstreamName = "fiber_manual_record",
                    codepoint = "e061",
                    style = "rounded",
                    axes = new
                    {
                        fill = 0,
                        weight = 500,
                        grade = 0,
                        opticalSize = 48,
                    },
                    rtlMirror = false,
                    interactive = true,
                    sourcePath = SourcePath,
                    sourceSha256 = SourceSha256,
                    outputPath = OutputPath,
                    outputSha256 = OutputSha256,
                    modified = true,
                    modificationNotice =
                        "Synthetic deterministic integration fixture.",
                    visibleLabelKey = "Recording_Start_Short",
                    accessibleNameKey =
                        "Recording_Start_AccessibleName",
                    tooltipKey = "Recording_Start_Tooltip",
                    surfaces = new[] { "wrist", "desktop" },
                },
            },
        };
        return JsonSerializer.Serialize(manifest) + "\n";
    }

    private static string Hash(string content) => Convert
        .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
        .ToLowerInvariant();
}
