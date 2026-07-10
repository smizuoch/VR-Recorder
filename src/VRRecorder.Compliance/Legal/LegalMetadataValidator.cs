namespace VRRecorder.Compliance.Legal;

public static class LegalMetadataValidator
{
    public static IReadOnlyList<ComplianceIssue> Validate(
        IEnumerable<ThirdPartyComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var issues = new List<ComplianceIssue>();

        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component.CopyrightNotice))
            {
                issues.Add(new ComplianceIssue(
                    "missing-copyright-notice",
                    component.Id));
            }

            if (component.LicenseFiles.Count == 0)
            {
                issues.Add(new ComplianceIssue(
                    "missing-license-text",
                    component.Id));
            }
        }

        return issues;
    }
}
