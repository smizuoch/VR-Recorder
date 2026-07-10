namespace VRRecorder.Compliance.Generation;

internal sealed class MaterialSymbolsManifestDocument
{
    public required int SchemaVersion { get; init; }

    public required string ComponentId { get; init; }

    public required string DisplayName { get; init; }

    public required MaterialSymbolsManifestSource Source { get; init; }

    public required MaterialSymbolsManifestRightsPolicy RightsPolicy
    {
        get;
        init;
    }

    public required MaterialSymbolsManifestRendering Rendering { get; init; }

    public required MaterialSymbolsManifestValidation Validation { get; init; }

    public required MaterialSymbolsManifestIcon[] SelectedIcons { get; init; }
}

internal sealed class MaterialSymbolsManifestSource
{
    public required string Repository { get; init; }

    public required string Browser { get; init; }

    public required string Commit { get; init; }

    public required string CodepointsFile { get; init; }

    public required string LicenseExpression { get; init; }

    public required string LicenseFile { get; init; }

    public required string AttributionFile { get; init; }

    public required string NoticeStatus { get; init; }

    public required string AcquisitionMode { get; init; }

    public required bool RuntimeNetworkAllowed { get; init; }
}

internal sealed class MaterialSymbolsManifestRightsPolicy
{
    public required string PermittedAssetClass { get; init; }

    public required bool AttributionDisplayedInLegalUi { get; init; }

    public required bool LicenseTextBundledOffline { get; init; }

    public required bool SourceAndOutputHashesRequired { get; init; }

    public required bool ModificationRecordRequired { get; init; }

    public required bool ExclusiveTrademarkUseAllowed { get; init; }

    public required bool ImplyGoogleAffiliationAllowed { get; init; }

    public required string[] ProhibitedAssetClasses { get; init; }
}

internal sealed class MaterialSymbolsManifestRendering
{
    public required string Family { get; init; }

    public required string SourceFormat { get; init; }

    public required string OutputFormat { get; init; }

    public required MaterialSymbolsManifestAxes DefaultAxes { get; init; }

    public required int PrimaryActionOpticalSize { get; init; }

    public required string ConversionTool { get; init; }

    public required string ConversionRecipe { get; init; }

    public required bool DeterministicOutputRequired { get; init; }

    public required bool ExternalFontFileAtRuntime { get; init; }
}

internal sealed class MaterialSymbolsManifestValidation
{
    public required bool FailOnUnknownUpstreamName { get; init; }

    public required bool FailOnCodepointMismatch { get; init; }

    public required bool FailOnUnregisteredOutputAsset { get; init; }

    public required bool FailOnMissingAccessibleNameForInteractiveIcon
    {
        get;
        init;
    }

    public required bool FailOnMissingTooltipForAmbiguousIconOnlyControl
    {
        get;
        init;
    }

    public required bool FailOnIncorrectRtlMirroring { get; init; }

    public required bool FailOnSourceOrOutputHashMismatch { get; init; }

    public required bool FailOnLicenseOrAttributionMismatch { get; init; }
}

internal sealed class MaterialSymbolsManifestIcon
{
    public required string SemanticId { get; init; }

    public required string UpstreamName { get; init; }

    public required string Codepoint { get; init; }

    public required string Style { get; init; }

    public MaterialSymbolsManifestAxes? Axes { get; init; }

    public Dictionary<string, MaterialSymbolsManifestAxes>? StateVariants
    {
        get;
        init;
    }

    public required bool RtlMirror { get; init; }

    public required bool Interactive { get; init; }

    public required string SourcePath { get; init; }

    public required string SourceSha256 { get; init; }

    public required string OutputPath { get; init; }

    public required string OutputSha256 { get; init; }

    public required bool Modified { get; init; }

    public string? ModificationNotice { get; init; }

    public string? VisibleLabelKey { get; init; }

    public string? AccessibleNameKey { get; init; }

    public string? TooltipKey { get; init; }

    public string? AccessibleDescriptionKey { get; init; }

    public bool? DecorativeWhenVisibleLabelPresent { get; init; }

    public required string[] Surfaces { get; init; }
}

internal sealed class MaterialSymbolsManifestAxes
{
    public required int Fill { get; init; }

    public required int Weight { get; init; }

    public required int Grade { get; init; }

    public required int OpticalSize { get; init; }
}
