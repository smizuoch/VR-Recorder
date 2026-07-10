namespace VRRecorder.Compliance.Dependencies;

public static class NuGetInventoryValidator
{
    public static IReadOnlyList<ComplianceIssue> Validate(
        IEnumerable<NuGetPackage> packages,
        IEnumerable<RegisteredNuGetPackage> registry)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(registry);

        var registeredIdentities = registry
            .Select(package => package.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return packages
            .Where(package => !registeredIdentities.Contains(package.Identity))
            .Select(package => new ComplianceIssue(
                "missing-component-registration",
                package.Identity))
            .ToArray();
    }
}
