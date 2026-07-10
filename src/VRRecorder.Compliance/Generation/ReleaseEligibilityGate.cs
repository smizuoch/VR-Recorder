namespace VRRecorder.Compliance.Generation;

public static class ReleaseEligibilityGate
{
    public static ReleaseEligibilityResult Evaluate(
        NormalizedComponentGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(graph.Components);

        var issues = new List<ComplianceIssue>();
        foreach (var component in graph.Components)
        {
            ArgumentNullException.ThrowIfNull(component.Approval);
            if (component.Approval.Status == LegalApprovalStatus.Pending)
            {
                issues.Add(new ComplianceIssue(
                    "pending-independent-review",
                    component.Id));
                continue;
            }

            if (component.Approval.Status != LegalApprovalStatus.Approved)
            {
                issues.Add(new ComplianceIssue(
                    "component-not-approved",
                    component.Id));
                continue;
            }

            if (string.IsNullOrWhiteSpace(component.Approval.TicketId))
            {
                issues.Add(new ComplianceIssue(
                    "missing-approval-ticket",
                    component.Id));
            }

            if (string.IsNullOrWhiteSpace(component.Approval.Reviewer))
            {
                issues.Add(new ComplianceIssue(
                    "missing-approval-reviewer",
                    component.Id));
            }

            if (string.IsNullOrWhiteSpace(component.Approval.RequestedBy))
            {
                issues.Add(new ComplianceIssue(
                    "missing-approval-requester",
                    component.Id));
            }
        }

        var orderedIssues = issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();

        return orderedIssues.Length == 0
            ? new ReleaseEligibilityResult(
                new ApprovedReleaseGraph(graph),
                [])
            : new ReleaseEligibilityResult(null, orderedIssues);
    }
}
