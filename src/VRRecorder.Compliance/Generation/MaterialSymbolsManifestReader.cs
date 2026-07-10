using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRRecorder.Compliance.Generation;

internal static class MaterialSymbolsManifestReader
{
    private const string ComponentId = "material-symbols";
    private const string ManifestPath = "MATERIAL-SYMBOLS-MANIFEST.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static bool TryRead(
        IReadOnlyList<NormalizedComponent> components,
        out MaterialSymbolsManifestDocument? manifest)
    {
        manifest = null;
        var materialComponents = components.Where(component =>
                string.Equals(component.Id, ComponentId, StringComparison.Ordinal))
            .ToArray();
        if (materialComponents.Length != 1)
        {
            return false;
        }

        var manifests = materialComponents[0].LegalFiles.Where(file =>
                file.Kind == LegalFileKind.AssetManifest &&
                string.Equals(
                    file.RelativePath,
                    ManifestPath,
                    StringComparison.Ordinal))
            .ToArray();
        return manifests.Length == 1 &&
               TryParse(manifests[0].Utf8Content, out manifest);
    }

    public static bool TryParse(
        string utf8Content,
        out MaterialSymbolsManifestDocument? manifest)
    {
        manifest = null;
        try
        {
            using var json = JsonDocument.Parse(utf8Content);
            if (ContainsDuplicateProperty(json.RootElement))
            {
                return false;
            }

            manifest = json.RootElement.Deserialize<
                MaterialSymbolsManifestDocument>(
                JsonOptions);
            return manifest is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!propertyNames.Add(property.Name) ||
                    ContainsDuplicateProperty(property.Value))
                {
                    return true;
                }
            }

            return false;
        }

        return element.ValueKind == JsonValueKind.Array &&
               element.EnumerateArray().Any(ContainsDuplicateProperty);
    }
}
