namespace VRRecorder.Compliance.Staging;

public sealed record MaterialSymbolsRightsLedgerEntry(
    string Id,
    string PathGlob,
    string ComponentRef,
    string Upstream,
    string Commit,
    string SelectedAssetManifest,
    string License,
    string Evidence,
    bool TrademarkUse,
    bool ProductLogoUse,
    bool RuntimeNetworkAllowed,
    bool RedistributionApproved,
    string ApprovalId);
