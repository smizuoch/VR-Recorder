namespace VRRecorder.Compliance.Distribution;

internal sealed record DistributionPromotionResult(
    bool Allowed,
    bool PublishEligible,
    IReadOnlyList<ComplianceIssue> Issues);
