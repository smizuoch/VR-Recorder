namespace VRRecorder.Compliance.Packaging;

public sealed record PackageGenerationResult(
    bool Succeeded,
    IReadOnlyList<ComplianceIssue> Issues);
