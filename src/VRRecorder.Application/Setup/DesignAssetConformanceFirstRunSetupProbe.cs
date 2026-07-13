using System.Text.Json;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class DesignAssetConformanceFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private const string MaterialManifest =
        "MATERIAL-SYMBOLS-MANIFEST.json";
    private const string M3Report = "M3-CONFORMANCE-REPORT.json";
    private readonly ILegalCatalogReader _reader;

    public DesignAssetConformanceFirstRunSetupProbe(ILegalCatalogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.DesignAssetConformance)
        {
            return false;
        }

        var catalogRead = await _reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (catalogRead is not LegalCatalogReadResult.Available available)
        {
            return false;
        }

        var material = FindDocument(available.Catalog, MaterialManifest);
        var m3 = FindDocument(available.Catalog, M3Report);
        if (material is null || m3 is null)
        {
            return false;
        }

        var materialText = await ReadTextAsync(material, cancellationToken)
            .ConfigureAwait(false);
        var m3Text = await ReadTextAsync(m3, cancellationToken)
            .ConfigureAwait(false);
        return materialText is not null && m3Text is not null &&
               IsApprovedMaterialManifest(materialText) &&
               IsEligibleM3Report(m3Text);
    }

    private async Task<string?> ReadTextAsync(
        CatalogDocument document,
        CancellationToken cancellationToken)
    {
        var read = await _reader.ReadDocumentAsync(
                document.ComponentId,
                document.Reference,
                cancellationToken)
            .ConfigureAwait(false);
        return read is LegalTextReadResult.Available available &&
               string.Equals(
                   available.Document.ComponentId,
                   document.ComponentId,
                   StringComparison.Ordinal) &&
               available.Document.Reference == document.Reference
            ? available.Document.Text
            : null;
    }

    private static CatalogDocument? FindDocument(
        LegalCatalogSnapshot catalog,
        string relativePath)
    {
        var matches = catalog.Components.SelectMany(component =>
                component.LegalDocuments
                    .Where(reference =>
                        reference.Kind == LegalDocumentKind.AssetManifest &&
                        string.Equals(
                            reference.RelativePath,
                            relativePath,
                            StringComparison.Ordinal))
                    .Select(reference => new CatalogDocument(
                        component.Id,
                        reference)))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsApprovedMaterialManifest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   ReadInt32(root, "schemaVersion") == 2 &&
                   string.Equals(
                       ReadString(root, "documentStatus"),
                       "APPROVED RELEASE MANIFEST",
                       StringComparison.Ordinal) &&
                   string.Equals(
                       ReadString(root, "componentId"),
                       "material-symbols",
                       StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsEligibleM3Report(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                ReadInt32(root, "schemaVersion") != 2 ||
                ReadBoolean(root, "evaluated") != true ||
                ReadBoolean(root, "releaseEligible") != true ||
                !root.TryGetProperty("summary", out var summary) ||
                summary.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return ReadInt32(summary, "sourceInventoryCoveragePercent") == 100 &&
                   ReadInt32(summary, "unclassifiedSourceEntries") == 0 &&
                   ReadInt32(summary, "deferredEntriesForShippedFeatures") == 0 &&
                   ReadInt32(summary, "unresolvedDeviations") == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? ReadInt32(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) &&
        property.TryGetInt32(out var result)
            ? result
            : null;

    private static bool? ReadBoolean(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record CatalogDocument(
        string ComponentId,
        LegalDocumentReference Reference);
}
