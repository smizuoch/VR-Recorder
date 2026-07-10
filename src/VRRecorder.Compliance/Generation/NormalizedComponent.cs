namespace VRRecorder.Compliance.Generation;

public sealed record NormalizedComponent(
    string Id,
    string DisplayName,
    string Version,
    LicenseDecision License,
    string CopyrightNotice,
    string Usage,
    string Linkage,
    bool Modified,
    string SourceInformation,
    string LicenseText,
    NoticeScope Scope,
    LegalApproval Approval,
    IReadOnlyList<NoticePackage> Packages);
