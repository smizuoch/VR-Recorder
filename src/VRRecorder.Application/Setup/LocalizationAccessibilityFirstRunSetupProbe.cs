using System.Text.Json;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class LocalizationAccessibilityFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private const string LocalizationContract = "LOCALIZATION-CONTRACT.json";
    private const string M3Report = "M3-CONFORMANCE-REPORT.json";
    private static readonly string[] RequiredChecks =
    [
        "tooltip-test",
        "accessible-name-test",
        "japanese-english-golden-test",
        "pseudo-locale-golden-test",
        "rtl-golden-test",
        "high-contrast-golden-test",
    ];
    private readonly ILegalCatalogReader _reader;

    public LocalizationAccessibilityFirstRunSetupProbe(
        ILegalCatalogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.LocalizationAccessibility)
        {
            return false;
        }

        var read = await _reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (read is not LegalCatalogReadResult.Available available)
        {
            return false;
        }

        var localization = FindDocument(
            available.Catalog,
            LocalizationContract);
        var m3 = FindDocument(available.Catalog, M3Report);
        if (localization is null || m3 is null)
        {
            return false;
        }

        var localizationText = await ReadTextAsync(
                localization,
                cancellationToken)
            .ConfigureAwait(false);
        var m3Text = await ReadTextAsync(m3, cancellationToken)
            .ConfigureAwait(false);
        return localizationText is not null && m3Text is not null &&
               IsValidLocalizationContract(localizationText) &&
               IsValidAccessibilityReport(m3Text);
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
        string path)
    {
        var matches = catalog.Components.SelectMany(component =>
                component.LegalDocuments
                    .Where(reference =>
                        reference.Kind == LegalDocumentKind.AssetManifest &&
                        string.Equals(
                            reference.RelativePath,
                            path,
                            StringComparison.Ordinal))
                    .Select(reference => new CatalogDocument(
                        component.Id,
                        reference)))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsValidLocalizationContract(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (ReadInt32(root, "schemaVersion") != 1 ||
                !HasStrings(root, "initialLocales", ["ja-JP", "en-US"]) ||
                !string.Equals(
                    ReadString(root, "fallbackLocale"),
                    "en-US",
                    StringComparison.Ordinal) ||
                !TryObject(root, "resourceRules", out var resourceRules) ||
                ReadBoolean(resourceRules, "hardCodedUserVisibleTextAllowed") != false ||
                !string.Equals(
                    ReadString(resourceRules, "missingKeyBehavior"),
                    "fail-build",
                    StringComparison.Ordinal) ||
                ReadBoolean(resourceRules, "exposeResourceKeyToUser") != false ||
                ReadBoolean(resourceRules, "iconLigatureAsAccessibleNameAllowed") != false ||
                !TryObject(root, "resourceParityRules", out var parity) ||
                ReadBoolean(parity, "sameKeySetRequired") != true ||
                ReadBoolean(parity, "placeholderParityRequired") != true ||
                ReadBoolean(parity, "emptyTranslationAllowed") != false ||
                !TryObject(root, "inputEquivalence", out var input) ||
                ReadBoolean(input, "dragOnlyOperationAllowed") != false ||
                ReadBoolean(input, "keyboardControllerRayParityRequired") != true)
            {
                return false;
            }

            return HasLayout(root, "ja-JP", 100, "ltr") &&
                   HasLayout(root, "en-US", 100, "ltr") &&
                   HasLayout(root, "qps-ploc", 200, "ltr") &&
                   HasLayout(root, "qps-plocm", 200, "rtl");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidAccessibilityReport(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (ReadInt32(root, "schemaVersion") != 2 ||
                ReadBoolean(root, "evaluated") != true ||
                ReadBoolean(root, "releaseEligible") != true ||
                !TryObject(root, "summary", out var summary) ||
                ReadInt32(summary, "accessibleNameCoveragePercent") != 100 ||
                !root.TryGetProperty("requiredChecks", out var checks) ||
                checks.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var present = checks.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            return RequiredChecks.All(present.Contains);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasLayout(
        JsonElement root,
        string locale,
        int scale,
        string direction) =>
        root.TryGetProperty("layoutTests", out var layouts) &&
        layouts.ValueKind == JsonValueKind.Array &&
        layouts.EnumerateArray().Any(item =>
            string.Equals(ReadString(item, "locale"), locale, StringComparison.Ordinal) &&
            ReadInt32(item, "scalePercent") == scale &&
            string.Equals(ReadString(item, "direction"), direction, StringComparison.Ordinal));

    private static bool HasStrings(
        JsonElement root,
        string name,
        IReadOnlyList<string> required) =>
        root.TryGetProperty(name, out var values) &&
        values.ValueKind == JsonValueKind.Array &&
        required.All(value => values.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String &&
            string.Equals(item.GetString(), value, StringComparison.Ordinal)));

    private static bool TryObject(
        JsonElement root,
        string name,
        out JsonElement value) =>
        root.TryGetProperty(name, out value) &&
        value.ValueKind == JsonValueKind.Object;

    private static int? ReadInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private static bool? ReadBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record CatalogDocument(
        string ComponentId,
        LegalDocumentReference Reference);
}
