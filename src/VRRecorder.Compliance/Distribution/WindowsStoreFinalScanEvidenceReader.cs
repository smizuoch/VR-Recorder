using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsStoreFinalScanEvidence(
    string PackageSha256,
    string LegalManifestSha256,
    string SbomSha256,
    string Scanner,
    string ScannerVersion,
    string DefinitionVersion,
    bool MalwareScanPassed,
    bool LegalBundleVerified,
    bool SbomPresent,
    bool PrivateKeysAbsent,
    DateTimeOffset CapturedAtUtc);

internal static class WindowsStoreFinalScanEvidenceReader
{
    private const int MaximumEvidenceBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "evidenceKind",
        "packageSha256",
        "legalManifestSha256",
        "sbomSha256",
        "scanner",
        "scannerVersion",
        "definitionVersion",
        "malwareScanPassed",
        "legalBundleVerified",
        "sbomPresent",
        "privateKeysAbsent",
        "capturedAtUtc",
    ];

    public static WindowsStoreFinalScanEvidence Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0 || content.Length > MaximumEvidenceBytes)
        {
            throw Invalid();
        }

        try
        {
            using var document = JsonDocument.Parse(
                StrictUtf8.GetString(content),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 4,
                });
            var root = document.RootElement;
            RequireExactProperties(root);
            if (RequiredInt32(root, "schemaVersion") != 1 ||
                RequiredString(root, "evidenceKind") !=
                "store-final-scan-v1")
            {
                throw Invalid();
            }

            var packageSha256 = RequiredSha256(root, "packageSha256");
            var legalManifestSha256 = RequiredSha256(
                root,
                "legalManifestSha256");
            var sbomSha256 = RequiredSha256(root, "sbomSha256");
            var capturedAt = RequiredString(root, "capturedAtUtc");
            if (!DateTimeOffset.TryParseExact(
                    capturedAt,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var capturedAtUtc))
            {
                throw Invalid();
            }

            return new WindowsStoreFinalScanEvidence(
                packageSha256,
                legalManifestSha256,
                sbomSha256,
                RequiredString(root, "scanner"),
                RequiredString(root, "scannerVersion"),
                RequiredString(root, "definitionVersion"),
                RequiredBoolean(root, "malwareScanPassed"),
                RequiredBoolean(root, "legalBundleVerified"),
                RequiredBoolean(root, "sbomPresent"),
                RequiredBoolean(root, "privateKeysAbsent"),
                capturedAtUtc);
        }
        catch (Exception exception) when (exception is
            JsonException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or
            ArgumentException)
        {
            throw new InvalidDataException(
                "The Store final-scan evidence is invalid.",
                exception);
        }
    }

    private static void RequireExactProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }

        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
            actual.Length != RootProperties.Length ||
            RootProperties.Any(name =>
                !actual.Contains(name, StringComparer.Ordinal)))
        {
            throw Invalid();
        }
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        return value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : throw Invalid();
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        return string.IsNullOrWhiteSpace(value) ? throw Invalid() : value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : throw Invalid();
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid(),
        };
    }

    private static InvalidDataException Invalid() => new();
}
