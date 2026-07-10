namespace VRRecorder.Compliance.Generation;

public sealed record ReleaseEligibilityResult(
    ApprovedReleaseGraph? ApprovedGraph,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsApproved => ApprovedGraph is not null && Issues.Count == 0;
}
