using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Generation;

public static class ThirdPartyComponentsGenerator
{
    public static string Generate(
        SpdxGenerationContext context,
        ApprovedReleaseGraph approvedGraph)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(approvedGraph);
        ValidateContext(context);
        var components = approvedGraph.Graph.Components
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .Select(ResolveComponent)
            .ToArray();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions { Indented = true }))
        {
            WriteDocument(writer, context, components);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + '\n';
    }

    private static void ValidateContext(SpdxGenerationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ProductVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.DocumentNamespace);
        if (!Uri.TryCreate(
                context.DocumentNamespace,
                UriKind.Absolute,
                out _))
        {
            throw new ArgumentException(
                "The component-catalog bundle ID must be an absolute URI.",
                nameof(context));
        }

        if (context.CreatedAtUtc.Offset != TimeSpan.Zero ||
            context.CreatedAtUtc.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException(
                "The component-catalog generation time must be UTC with whole-second precision.",
                nameof(context));
        }
    }

    private static CatalogComponent ResolveComponent(
        NormalizedComponent component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.Version);
        ArgumentNullException.ThrowIfNull(component.License);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            component.License.ConcludedExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.Usage);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.Linkage);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.SourceInformation);
        ArgumentNullException.ThrowIfNull(component.LegalFiles);
        var licenses = component.LegalFiles
            .Where(file => file.Kind == LegalFileKind.License)
            .ToArray();
        if (licenses.Length != 1)
        {
            throw new InvalidOperationException(
                $"Component {component.Id} must reference exactly one verified license file.");
        }

        return new CatalogComponent(
            component.Id,
            component.DisplayName,
            component.Version,
            component.License.ConcludedExpression,
            component.Usage,
            component.Linkage,
            component.Modified,
            licenses[0].RelativePath,
            component.SourceInformation);
    }

    private static void WriteDocument(
        Utf8JsonWriter writer,
        SpdxGenerationContext context,
        IReadOnlyList<CatalogComponent> components)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("bundleId", context.DocumentNamespace);
        writer.WriteString("productVersion", context.ProductVersion);
        writer.WriteString(
            "generatedAtUtc",
            context.CreatedAtUtc.ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture));
        writer.WritePropertyName("integrityManifest");
        writer.WriteStartObject();
        writer.WriteString("path", "LEGAL-MANIFEST.sha256");
        writer.WriteString("algorithm", "SHA-256");
        writer.WriteEndObject();
        writer.WritePropertyName("components");
        writer.WriteStartArray();
        foreach (var component in components)
        {
            writer.WriteStartObject();
            writer.WriteString("id", component.Id);
            writer.WriteString("displayName", component.DisplayName);
            writer.WriteString("version", component.Version);
            writer.WriteString(
                "licenseExpression",
                component.LicenseExpression);
            writer.WriteString("usage", component.Usage);
            writer.WriteString("linkage", component.Linkage);
            writer.WriteBoolean("modified", component.Modified);
            writer.WriteString("licenseText", component.LicenseText);
            writer.WriteString("sourceInfo", component.SourceInfo);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private sealed record CatalogComponent(
        string Id,
        string DisplayName,
        string Version,
        string LicenseExpression,
        string Usage,
        string Linkage,
        bool Modified,
        string LicenseText,
        string SourceInfo);
}
