namespace VRRecorder.Compliance.Generation;

public static class ReleaseEligibilityGate
{
    public static ReleaseEligibilityResult Evaluate(
        NormalizedComponentGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(graph.Components);

        var issues = graph.Components
            .Where(component =>
                component.ApprovalStatus != LegalApprovalStatus.Approved)
            .Select(component => new ComplianceIssue(
                component.ApprovalStatus == LegalApprovalStatus.Pending
                    ? "pending-independent-review"
                    : "component-not-approved",
                component.Id))
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();

        return issues.Length == 0
            ? new ReleaseEligibilityResult(
                new ApprovedReleaseGraph(graph),
                [])
            : new ReleaseEligibilityResult(null, issues);
    }
}
