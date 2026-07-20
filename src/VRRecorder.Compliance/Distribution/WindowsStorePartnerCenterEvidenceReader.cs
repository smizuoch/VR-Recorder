using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsStorePartnerCenterEvidence(
    string PackageSha256,
    string SubmissionId,
    string CertificationReportSha256,
    string FlightReportSha256,
    string ApprovedBy,
    DateTimeOffset CapturedAtUtc);

internal static class WindowsStorePartnerCenterEvidenceReader
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
        "submissionId",
        "certificationStatus",
        "certificationReportSha256",
        "flightStatus",
        "flightReportSha256",
        "approvedBy",
        "capturedAtUtc",
    ];

    public static WindowsStorePartnerCenterEvidence Read(byte[] content)
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
            var capturedAt = RequiredString(root, "capturedAtUtc");
            if (RequiredInt32(root, "schemaVersion") != 1 ||
                RequiredString(root, "evidenceKind") !=
                "partner-center-public-release-v1" ||
                RequiredString(root, "certificationStatus") != "passed" ||
                RequiredString(root, "flightStatus") != "passed" ||
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

            return new WindowsStorePartnerCenterEvidence(
                RequiredSha256(root, "packageSha256"),
                RequiredString(root, "submissionId"),
                RequiredSha256(root, "certificationReportSha256"),
                RequiredSha256(root, "flightReportSha256"),
                RequiredString(root, "approvedBy"),
                capturedAtUtc);
        }
        catch (Exception exception) when (exception is
            JsonException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or
            ArgumentException)
        {
            throw new InvalidDataException(
                "The Partner Center public-release evidence is invalid.",
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

    private static InvalidDataException Invalid() => new();
}
