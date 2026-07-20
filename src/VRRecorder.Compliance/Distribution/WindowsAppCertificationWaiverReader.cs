using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsAppCertificationWaiver(
    string PackageSha256,
    string ToolVersion,
    string Reason,
    string RequestedBy,
    string ApprovedBy,
    string FlightSubmissionId,
    DateTimeOffset CapturedAtUtc);

internal static class WindowsAppCertificationWaiverReader
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
        "toolVersion",
        "reason",
        "requestedBy",
        "approvedBy",
        "flightSubmissionId",
        "flightCertificationStatus",
        "flightValidationStatus",
        "capturedAtUtc",
    ];

    public static WindowsAppCertificationWaiver Read(byte[] content)
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
            var requestedBy = RequiredString(root, "requestedBy");
            var approvedBy = RequiredString(root, "approvedBy");
            var packageSha256 = RequiredString(root, "packageSha256");
            var capturedAt = RequiredString(root, "capturedAtUtc");
            if (RequiredInt32(root, "schemaVersion") != 1 ||
                RequiredString(root, "evidenceKind") !=
                "wack-tool-unavailable-waiver-v1" ||
                !IsLowerHexSha256(packageSha256) ||
                string.Equals(
                    requestedBy,
                    approvedBy,
                    StringComparison.OrdinalIgnoreCase) ||
                RequiredString(root, "flightCertificationStatus") !=
                "passed" ||
                RequiredString(root, "flightValidationStatus") != "passed" ||
                !DateTimeOffset.TryParseExact(
                    capturedAt,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var capturedAtUtc))
            {
                throw Invalid();
            }

            return new WindowsAppCertificationWaiver(
                packageSha256,
                RequiredString(root, "toolVersion"),
                RequiredString(root, "reason"),
                requestedBy,
                approvedBy,
                RequiredString(root, "flightSubmissionId"),
                capturedAtUtc);
        }
        catch (Exception exception) when (exception is
            JsonException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or
            ArgumentException)
        {
            throw new InvalidDataException(
                "The WACK tool-unavailable waiver is invalid.",
                exception);
        }
    }

    private static void RequireExactProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }

        var actual = root.EnumerateObject()
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

    private static string RequiredString(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        return string.IsNullOrWhiteSpace(value) ||
               value.Length > 512 ||
               value.Any(character => character is < ' ' or > '~')
            ? throw Invalid()
            : value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : throw Invalid();
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InvalidDataException Invalid() => new();
}
