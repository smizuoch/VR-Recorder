namespace VRRecorder.Compliance.Legal;

public sealed record ThirdPartyComponent(
    string Id,
    string LicenseConcluded,
    string CopyrightNotice,
    IReadOnlyList<LegalFileReference> LicenseFiles);
