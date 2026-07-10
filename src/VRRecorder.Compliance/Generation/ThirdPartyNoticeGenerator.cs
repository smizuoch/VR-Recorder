using System.Text;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Generation;

public static class ThirdPartyNoticeGenerator
{
    public static string Generate(
        string productName,
        IEnumerable<NuGetPackage> dependencies,
        IEnumerable<NoticeComponent> components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(components);

        var dependencyArray = dependencies.ToArray();
        var selectedComponents = SelectDistributedComponents(
            dependencyArray,
            components.ToArray());
        var output = new StringBuilder();
        output.Append(productName).Append(" THIRD-PARTY NOTICES\n\n");

        foreach (var component in selectedComponents)
        {
            output.Append("Component: ").Append(component.DisplayName).Append('\n');
            output.Append("Version: ").Append(component.Version).Append('\n');
            output.Append("SPDX: ").Append(component.LicenseExpression).Append('\n');
            output.Append("Copyright: ")
                .Append(component.CopyrightNotice)
                .Append('\n');
            output.Append("Usage: ").Append(component.Usage).Append('\n');
            output.Append("Linkage: ").Append(component.Linkage).Append('\n');
            output.Append("Modified: ")
                .Append(component.Modified ? "yes" : "no")
                .Append('\n');
            output.Append("Source: ")
                .Append(component.SourceInformation)
                .Append('\n');

            foreach (var dependency in DependenciesFor(
                         component,
                         dependencyArray))
            {
                output.Append("Dependency: ")
                    .Append(dependency.Identity)
                    .Append(" (")
                    .Append(dependency.Kind)
                    .Append(")\n");
            }

            output.Append("\n--- LICENSE TEXT ---\n")
                .Append(component.LicenseText.TrimEnd())
                .Append("\n--- END LICENSE TEXT ---\n\n");
        }

        return output.ToString();
    }

    private static NoticeComponent[] SelectDistributedComponents(
        IReadOnlyList<NuGetPackage> dependencies,
        IReadOnlyList<NoticeComponent> components)
    {
        var selected = new Dictionary<string, NoticeComponent>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in dependencies)
        {
            var matches = components
                .Where(component => component.Packages.Any(package =>
                    string.Equals(
                        package.Identity,
                        dependency.Identity,
                        StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Dependency {dependency.Identity} must map to exactly one component.");
            }

            var component = matches[0];
            if (component.Scope is NoticeScope.TestOnly or NoticeScope.BuildOnly)
            {
                continue;
            }

            if (component.ApprovalStatus != LegalApprovalStatus.Approved)
            {
                throw new InvalidOperationException(
                    $"Component {component.Id} is not approved for release notices.");
            }

            selected.TryAdd(component.Id, component);
        }

        return selected.Values
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static NuGetPackage[] DependenciesFor(
        NoticeComponent component,
        IEnumerable<NuGetPackage> dependencies) =>
        dependencies
            .Where(dependency => component.Packages.Any(package =>
                string.Equals(
                    package.Identity,
                    dependency.Identity,
                    StringComparison.OrdinalIgnoreCase)))
            .OrderBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependency => dependency.Version, StringComparer.Ordinal)
            .ToArray();
}
