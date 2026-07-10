namespace VRRecorder.Compliance.Staging;

public sealed record MaterialSymbolsStagedAssetRegistration(
    string OutputPath,
    string StagingRelativePath,
    string RightsLedgerEntryId);
