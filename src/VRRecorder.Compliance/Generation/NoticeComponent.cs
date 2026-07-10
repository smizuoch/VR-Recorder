namespace VRRecorder.Compliance.Generation;

public sealed record NoticeComponent(
    string Id,
    string DisplayName,
    string Version,
    string LicenseExpression,
    string CopyrightNotice,
    string Usage,
    string Linkage,
    bool Modified,
    string SourceInformation,
    string LicenseText,
    NoticeScope Scope,
    LegalApprovalStatus ApprovalStatus,
    IReadOnlyList<NoticePackage> Packages);
