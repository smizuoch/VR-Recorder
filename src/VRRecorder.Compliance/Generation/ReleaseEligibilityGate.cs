using System.Security.Cryptography;
using System.Text;

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
            ArgumentNullException.ThrowIfNull(component.License);
            ArgumentNullException.ThrowIfNull(component.LegalFiles);
            if (IsUnresolved(component.License.DeclaredExpression) ||
                IsUnresolved(component.License.ConcludedExpression))
            {
                issues.Add(new ComplianceIssue(
                    "unresolved-license",
                    component.Id));
            }

            if (string.IsNullOrWhiteSpace(component.CopyrightNotice))
            {
                issues.Add(new ComplianceIssue(
                    "missing-copyright-notice",
                    component.Id));
            }

            var legalFiles = component.LegalFiles.ToArray();
            var licenseCount = legalFiles.Count(file =>
                file.Kind == LegalFileKind.License);
            if (licenseCount != 1)
            {
                issues.Add(new ComplianceIssue(
                    "invalid-license-document-count",
                    component.Id));
            }

            foreach (var duplicatePath in legalFiles
                         .GroupBy(
                             file => file.RelativePath,
                             StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Skip(1).Any()))
            {
                issues.Add(new ComplianceIssue(
                    "duplicate-legal-document-path",
                    $"{component.Id}:{duplicatePath.Key}"));
            }

            foreach (var legalFile in legalFiles)
            {
                if (!Enum.IsDefined(legalFile.Kind))
                {
                    issues.Add(new ComplianceIssue(
                        "unknown-legal-document-kind",
                        $"{component.Id}:{(int)legalFile.Kind}"));
                    continue;
                }

                try
                {
                    _ = LegalArtifactPath.Resolve(
                        Path.GetTempPath(),
                        legalFile.RelativePath);
                }
                catch (ArgumentException)
                {
                    issues.Add(new ComplianceIssue(
                        "invalid-legal-document-path",
                        $"{component.Id}:{legalFile.RelativePath}"));
                }

                if (legalFile.Kind == LegalFileKind.AssetManifest &&
                    !string.Equals(
                        legalFile.RelativePath,
                        "MATERIAL-SYMBOLS-MANIFEST.json",
                        StringComparison.Ordinal))
                {
                    issues.Add(new ComplianceIssue(
                        "invalid-asset-manifest-path",
                        $"{component.Id}:{legalFile.RelativePath}"));
                }

                var actualHash = Convert
                    .ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(legalFile.Utf8Content)))
                    .ToLowerInvariant();
                if (!string.Equals(
                        legalFile.Sha256,
                        actualHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ComplianceIssue(
                        "legal-file-hash-mismatch",
                        $"{component.Id}:{legalFile.RelativePath}"));
                }
            }

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

            if (!string.IsNullOrWhiteSpace(component.Approval.RequestedBy) &&
                !string.IsNullOrWhiteSpace(component.Approval.Reviewer) &&
                string.Equals(
                    component.Approval.RequestedBy,
                    component.Approval.Reviewer,
                    StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ComplianceIssue(
                    "self-approval",
                    component.Id));
            }
        }

        foreach (var collision in graph.Components
                     .SelectMany(component => component.LegalFiles.Select(file =>
                         new ComponentLegalPath(
                             component.Id,
                             file.RelativePath)))
                     .GroupBy(
                         item => item.RelativePath,
                         StringComparer.OrdinalIgnoreCase)
                     .Where(group => group
                         .Select(item => item.ComponentId)
                         .Distinct(StringComparer.Ordinal)
                         .Skip(1)
                         .Any()))
        {
            var componentIds = collision
                .Select(item => item.ComponentId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal);
            var canonicalPath = collision
                .Select(item => item.RelativePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .First();
            issues.Add(new ComplianceIssue(
                "duplicate-legal-document-path",
                $"{string.Join(',', componentIds)}:{canonicalPath}"));
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

    private static bool IsUnresolved(string expression) =>
        string.Equals(expression, "UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            expression,
            "NOASSERTION",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(expression, "NONE", StringComparison.OrdinalIgnoreCase);

    private sealed record ComponentLegalPath(
        string ComponentId,
        string RelativePath);
}
