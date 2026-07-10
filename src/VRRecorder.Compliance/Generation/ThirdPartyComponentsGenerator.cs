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
        ArgumentException.ThrowIfNullOrWhiteSpace(component.CopyrightNotice);
        ArgumentNullException.ThrowIfNull(component.LegalFiles);
        var legalFiles = component.LegalFiles.ToArray();
        if (legalFiles.Any(file => !Enum.IsDefined(file.Kind)))
        {
            throw new InvalidOperationException(
                $"Component {component.Id} contains an unknown legal-document kind.");
        }

        var duplicatePath = legalFiles
            .GroupBy(file => file.RelativePath, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicatePath is not null)
        {
            throw new InvalidOperationException(
                $"Component {component.Id} references legal-document path {duplicatePath.Key} more than once.");
        }

        var licenses = legalFiles
            .Where(file => file.Kind == LegalFileKind.License)
            .ToArray();
        if (licenses.Length != 1)
        {
            throw new InvalidOperationException(
                $"Component {component.Id} must reference exactly one verified license file.");
        }

        foreach (var file in legalFiles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(file.RelativePath);
            if (file.Kind == LegalFileKind.AssetManifest &&
                !string.Equals(
                    file.RelativePath,
                    "MATERIAL-SYMBOLS-MANIFEST.json",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Component {component.Id} has a non-canonical asset-manifest reference.");
            }
        }

        return new CatalogComponent(
            component.Id,
            component.DisplayName,
            component.Version,
            component.License.ConcludedExpression,
            component.Usage,
            component.Linkage,
            component.Modified,
            component.SourceInformation,
            component.CopyrightNotice,
            legalFiles
                .OrderBy(file => KindRank(file.Kind))
                .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new CatalogDocument(
                    KindName(file.Kind),
                    file.RelativePath))
                .ToArray());
    }

    private static void WriteDocument(
        Utf8JsonWriter writer,
        SpdxGenerationContext context,
        IReadOnlyList<CatalogComponent> components)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 3);
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
            writer.WriteString("sourceInfo", component.SourceInfo);
            writer.WriteString(
                "copyrightNotice",
                component.CopyrightNotice);
            writer.WritePropertyName("legalDocuments");
            writer.WriteStartArray();
            foreach (var document in component.LegalDocuments)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", document.Kind);
                writer.WriteString("path", document.Path);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
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
        string SourceInfo,
        string CopyrightNotice,
        IReadOnlyList<CatalogDocument> LegalDocuments);

    private sealed record CatalogDocument(string Kind, string Path);

    private static string KindName(LegalFileKind kind) => kind switch
    {
        LegalFileKind.License => "license",
        LegalFileKind.Notice => "notice",
        LegalFileKind.Copyright => "copyright",
        LegalFileKind.Attribution => "attribution",
        LegalFileKind.AssetManifest => "asset-manifest",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int KindRank(LegalFileKind kind) => kind switch
    {
        LegalFileKind.License => 0,
        LegalFileKind.Notice => 1,
        LegalFileKind.Copyright => 2,
        LegalFileKind.Attribution => 3,
        LegalFileKind.AssetManifest => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
