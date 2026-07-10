using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Generation;

public static class SpdxSbomGenerator
{
    public static string Generate(
        SpdxGenerationContext context,
        IEnumerable<NuGetPackage> dependencies,
        IEnumerable<NoticeComponent> components)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(components);
        ValidateContext(context);

        var packages = ResolvePackages(
            dependencies.ToArray(),
            components.ToArray());
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions { Indented = true }))
        {
            WriteDocument(writer, context, packages);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + '\n';
    }

    private static void ValidateContext(SpdxGenerationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProductName);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProductVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.DocumentNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Creator);

        if (!Uri.TryCreate(
                context.DocumentNamespace,
                UriKind.Absolute,
                out _))
        {
            throw new ArgumentException(
                "The SPDX document namespace must be an absolute URI.",
                nameof(context));
        }

        if (context.CreatedAtUtc.Offset != TimeSpan.Zero ||
            context.CreatedAtUtc.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException(
                "The SPDX creation time must be UTC with whole-second precision.",
                nameof(context));
        }

        if (!context.Creator.StartsWith("Person: ", StringComparison.Ordinal) &&
            !context.Creator.StartsWith(
                "Organization: ",
                StringComparison.Ordinal) &&
            !context.Creator.StartsWith("Tool: ", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The SPDX creator must identify a Person, Organization, or Tool.",
                nameof(context));
        }
    }

    private static ResolvedPackage[] ResolvePackages(
        NuGetPackage[] dependencies,
        NoticeComponent[] components)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spdxIds = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<ResolvedPackage>(dependencies.Length);

        foreach (var dependency in dependencies
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.Ordinal)
                     .ThenBy(item => item.Version, StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dependency.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(dependency.Version);
            if (!identities.Add(dependency.Identity))
            {
                throw new InvalidOperationException(
                    $"Dependency {dependency.Identity} appears more than once.");
            }

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
            if (component.ApprovalStatus != LegalApprovalStatus.Approved)
            {
                throw new InvalidOperationException(
                    $"Component {component.Id} is not approved for release SBOM generation.");
            }

            ValidatePackageMetadata(component);
            var spdxId = CreatePackageSpdxId(dependency);
            if (!spdxIds.Add(spdxId))
            {
                throw new InvalidOperationException(
                    $"Dependency {dependency.Identity} has a duplicate SPDX identifier.");
            }

            resolved.Add(new ResolvedPackage(dependency, component, spdxId));
        }

        return [.. resolved];
    }

    private static void ValidatePackageMetadata(NoticeComponent component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.LicenseExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.CopyrightNotice);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.SourceInformation);
    }

    private static string CreatePackageSpdxId(NuGetPackage dependency)
    {
        var value = $"{dependency.Id}-{dependency.Version}";
        var builder = new StringBuilder("SPDXRef-Package-");
        foreach (var character in value)
        {
            builder.Append(
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-'
                    ? character
                    : '-');
        }

        return builder.ToString();
    }

    private static void WriteDocument(
        Utf8JsonWriter writer,
        SpdxGenerationContext context,
        IReadOnlyList<ResolvedPackage> packages)
    {
        writer.WriteStartObject();
        writer.WriteString("spdxVersion", "SPDX-2.3");
        writer.WriteString("dataLicense", "CC0-1.0");
        writer.WriteString("SPDXID", "SPDXRef-DOCUMENT");
        writer.WriteString(
            "name",
            $"{context.ProductName}-{context.ProductVersion}");
        writer.WriteString("documentNamespace", context.DocumentNamespace);
        writer.WritePropertyName("creationInfo");
        writer.WriteStartObject();
        writer.WriteString(
            "created",
            context.CreatedAtUtc.ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture));
        writer.WritePropertyName("creators");
        writer.WriteStartArray();
        writer.WriteStringValue(context.Creator);
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("documentDescribes");
        writer.WriteStartArray();
        foreach (var package in packages)
        {
            writer.WriteStringValue(package.SpdxId);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("packages");
        writer.WriteStartArray();
        foreach (var package in packages)
        {
            WritePackage(writer, package);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("relationships");
        writer.WriteStartArray();
        foreach (var package in packages)
        {
            writer.WriteStartObject();
            writer.WriteString("spdxElementId", "SPDXRef-DOCUMENT");
            writer.WriteString("relationshipType", "DESCRIBES");
            writer.WriteString("relatedSpdxElement", package.SpdxId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePackage(
        Utf8JsonWriter writer,
        ResolvedPackage package)
    {
        writer.WriteStartObject();
        writer.WriteString("name", package.Dependency.Id);
        writer.WriteString("SPDXID", package.SpdxId);
        writer.WriteString("versionInfo", package.Dependency.Version);
        writer.WriteString(
            "downloadLocation",
            package.Component.SourceInformation);
        writer.WriteBoolean("filesAnalyzed", false);
        writer.WriteString(
            "licenseConcluded",
            package.Component.LicenseExpression);
        writer.WriteString(
            "licenseDeclared",
            package.Component.LicenseExpression);
        writer.WriteString(
            "copyrightText",
            package.Component.CopyrightNotice);
        writer.WritePropertyName("externalRefs");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("referenceCategory", "PACKAGE-MANAGER");
        writer.WriteString("referenceType", "purl");
        writer.WriteString(
            "referenceLocator",
            $"pkg:nuget/{Uri.EscapeDataString(package.Dependency.Id)}@{Uri.EscapeDataString(package.Dependency.Version)}");
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private sealed record ResolvedPackage(
        NuGetPackage Dependency,
        NoticeComponent Component,
        string SpdxId);
}
