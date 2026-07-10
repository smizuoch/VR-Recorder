namespace VRRecorder.Compliance.Dependencies;

public static class NuGetInventoryValidator
{
    public static IReadOnlyList<ComplianceIssue> Validate(
        IEnumerable<NuGetPackage> packages,
        IEnumerable<RegisteredNuGetPackage> registry)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(registry);

        var packagesById = registry.ToDictionary(
            package => package.Id,
            StringComparer.OrdinalIgnoreCase);
        var issues = new List<ComplianceIssue>();

        foreach (var package in packages)
        {
            if (!packagesById.TryGetValue(package.Id, out var registeredPackage))
            {
                issues.Add(new ComplianceIssue(
                    "missing-component-registration",
                    package.Identity));
            }
            else if (!string.Equals(
                         package.Version,
                         registeredPackage.Version,
                         StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ComplianceIssue(
                    "registry-version-mismatch",
                    package.Identity));
            }
        }

        return issues;
    }
}
