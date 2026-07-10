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
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(components);
        return GenerateCore(
            productName,
            dependencies.ToArray(),
            components.Select(ToRenderedComponent).ToArray(),
            showLicenseDecision: false);
    }

    public static string Generate(
        string productName,
        ApprovedReleaseGraph approvedGraph)
    {
        ArgumentNullException.ThrowIfNull(approvedGraph);
        return GenerateCore(
            productName,
            approvedGraph.Graph.Dependencies.ToArray(),
            approvedGraph.Graph.Components
                .Select(ToRenderedComponent)
                .ToArray(),
            showLicenseDecision: true);
    }

    private static string GenerateCore(
        string productName,
        NuGetPackage[] dependencies,
        RenderedComponent[] components,
        bool showLicenseDecision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        var selectedComponents = SelectDistributedComponents(
            dependencies,
            components);
        var output = new StringBuilder();
        output.Append(productName).Append(" THIRD-PARTY NOTICES\n\n");

        foreach (var component in selectedComponents)
        {
            output.Append("Component: ").Append(component.DisplayName).Append('\n');
            output.Append("Version: ").Append(component.Version).Append('\n');
            if (showLicenseDecision)
            {
                output.Append("SPDX declared: ")
                    .Append(component.LicenseDeclared)
                    .Append('\n');
                output.Append("SPDX concluded: ")
                    .Append(component.LicenseConcluded)
                    .Append('\n');
            }
            else
            {
                output.Append("SPDX: ")
                    .Append(component.LicenseConcluded)
                    .Append('\n');
            }

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
                         dependencies))
            {
                output.Append("Dependency: ")
                    .Append(dependency.Identity)
                    .Append(" (")
                    .Append(dependency.Kind)
                    .Append(")\n");
            }

            if (showLicenseDecision)
            {
                AppendLegalFiles(output, component.LegalFiles);
            }
            else
            {
                output.Append("\n--- LICENSE TEXT ---\n")
                    .Append(component.LicenseText);
                if (!component.LicenseText.EndsWith('\n'))
                {
                    output.Append('\n');
                }

                output.Append("--- END LICENSE TEXT ---\n\n");
            }
        }

        return output.ToString();
    }

    private static RenderedComponent[] SelectDistributedComponents(
        IReadOnlyList<NuGetPackage> dependencies,
        IReadOnlyList<RenderedComponent> components)
    {
        var selected = new Dictionary<string, RenderedComponent>(
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

            if (!component.IsApproved)
            {
                throw new InvalidOperationException(
                    $"Component {component.Id} is not approved for release notices.");
            }

            if (string.IsNullOrWhiteSpace(component.CopyrightNotice))
            {
                throw new InvalidOperationException(
                    $"Component {component.Id} is missing its copyright notice.");
            }

            selected.TryAdd(component.Id, component);
        }

        return selected.Values
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static NuGetPackage[] DependenciesFor(
        RenderedComponent component,
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

    private static RenderedComponent ToRenderedComponent(
        NoticeComponent component) =>
        new(
            component.Id,
            component.DisplayName,
            component.Version,
            component.LicenseExpression,
            component.LicenseExpression,
            component.CopyrightNotice,
            component.Usage,
            component.Linkage,
            component.Modified,
            component.SourceInformation,
            component.LicenseText,
            [],
            component.Scope,
            component.ApprovalStatus == LegalApprovalStatus.Approved,
            component.Packages);

    private static RenderedComponent ToRenderedComponent(
        NormalizedComponent component) =>
        new(
            component.Id,
            component.DisplayName,
            component.Version,
            component.License.DeclaredExpression,
            component.License.ConcludedExpression,
            component.CopyrightNotice,
            component.Usage,
            component.Linkage,
            component.Modified,
            component.SourceInformation,
            component.LicenseText,
            component.LegalFiles,
            component.Scope,
            component.Approval.Status == LegalApprovalStatus.Approved,
            component.Packages);

    private sealed record RenderedComponent(
        string Id,
        string DisplayName,
        string Version,
        string LicenseDeclared,
        string LicenseConcluded,
        string CopyrightNotice,
        string Usage,
        string Linkage,
        bool Modified,
        string SourceInformation,
        string LicenseText,
        IReadOnlyList<VerifiedLegalFile> LegalFiles,
        NoticeScope Scope,
        bool IsApproved,
        IReadOnlyList<NoticePackage> Packages);

    private static void AppendLegalFiles(
        StringBuilder output,
        IEnumerable<VerifiedLegalFile> legalFiles)
    {
        foreach (var legalFile in legalFiles
                     .OrderBy(file => file.Kind)
                     .ThenBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            output.Append("\n--- LEGAL FILE (")
                .Append(legalFile.Kind)
                .Append("): ")
                .Append(legalFile.RelativePath)
                .Append(" ---\n")
                .Append(legalFile.Utf8Content);
            if (!legalFile.Utf8Content.EndsWith('\n'))
            {
                output.Append('\n');
            }

            output.Append("--- END LEGAL FILE ---\n");
        }

        output.Append('\n');
    }
}
