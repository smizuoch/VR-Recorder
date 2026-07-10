namespace VRRecorder.Compliance.Legal;

public static class LegalMetadataValidator
{
    public static IReadOnlyList<ComplianceIssue> Validate(
        IEnumerable<ThirdPartyComponent> components,
        IReadOnlyDictionary<string, string> actualFileHashes)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(actualFileHashes);

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

            foreach (var licenseFile in component.LicenseFiles)
            {
                if (!actualFileHashes.TryGetValue(licenseFile.Path, out var actualHash))
                {
                    issues.Add(new ComplianceIssue(
                        "missing-license-text",
                        component.Id));
                }
                else if (!string.Equals(
                             licenseFile.Sha256,
                             actualHash,
                             StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ComplianceIssue(
                        "license-file-hash-mismatch",
                        component.Id));
                }
            }
        }

        return issues;
    }
}
