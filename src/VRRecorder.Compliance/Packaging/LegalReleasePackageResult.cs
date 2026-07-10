using VRRecorder.Compliance.Runtime;

namespace VRRecorder.Compliance.Packaging;

public sealed record LegalReleasePackageResult(
    bool Succeeded,
    IReadOnlyList<ComplianceIssue> Issues,
    AuthenticatedLegalBundleAnchor? AuthenticatedAnchor,
    string? LegalBundleRelativePath);
