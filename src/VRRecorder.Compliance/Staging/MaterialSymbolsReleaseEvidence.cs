namespace VRRecorder.Compliance.Staging;

public sealed record MaterialSymbolsReleaseEvidence(
    string RepositoryRoot,
    MaterialSymbolsRightsLedgerEntry RightsLedgerEntry,
    IReadOnlyList<MaterialSymbolsStagedAssetRegistration> StagedAssets);
