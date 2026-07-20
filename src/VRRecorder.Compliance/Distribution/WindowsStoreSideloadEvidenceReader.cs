using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsStoreSideloadEvidence(
    string PackageSha256,
    string ManifestPublisher,
    string CertificateSubject,
    string CertificateThumbprint,
    string SignToolVersion,
    bool SignatureVerified,
    bool InstallSucceeded,
    bool LaunchSucceeded,
    bool UninstallSucceeded,
    bool InstallRootReadOnly,
    bool WorkingDirectoryIndependent,
    bool SettingsPassed,
    bool DiagnosticsPassed,
    bool LegalDisplayPassed,
    DateTimeOffset CapturedAtUtc);

internal static class WindowsStoreSideloadEvidenceReader
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
        "manifestPublisher",
        "certificateSubject",
        "certificateThumbprint",
        "signToolVersion",
        "signatureVerified",
        "installSucceeded",
        "launchSucceeded",
        "uninstallSucceeded",
        "installRootReadOnly",
        "workingDirectoryIndependent",
        "settingsPassed",
        "diagnosticsPassed",
        "legalDisplayPassed",
        "capturedAtUtc",
    ];

    public static WindowsStoreSideloadEvidence Read(byte[] content)
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
            RequireExactProperties(root, RootProperties);
            if (RequiredInt32(root, "schemaVersion") != 1 ||
                RequiredString(root, "evidenceKind") !=
                "store-sideload-lifecycle-v1")
            {
                throw Invalid();
            }

            var packageSha256 = RequiredString(root, "packageSha256");
            var thumbprint = RequiredString(
                root,
                "certificateThumbprint");
            var capturedAt = RequiredString(root, "capturedAtUtc");
            if (!IsLowerHex(packageSha256, 64) ||
                !IsLowerHex(thumbprint, 40) ||
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

            return new WindowsStoreSideloadEvidence(
                packageSha256,
                RequiredString(root, "manifestPublisher"),
                RequiredString(root, "certificateSubject"),
                thumbprint,
                RequiredString(root, "signToolVersion"),
                RequiredBoolean(root, "signatureVerified"),
                RequiredBoolean(root, "installSucceeded"),
                RequiredBoolean(root, "launchSucceeded"),
                RequiredBoolean(root, "uninstallSucceeded"),
                RequiredBoolean(root, "installRootReadOnly"),
                RequiredBoolean(root, "workingDirectoryIndependent"),
                RequiredBoolean(root, "settingsPassed"),
                RequiredBoolean(root, "diagnosticsPassed"),
                RequiredBoolean(root, "legalDisplayPassed"),
                capturedAtUtc);
        }
        catch (Exception exception) when (exception is
            JsonException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or
            ArgumentException)
        {
            throw new InvalidDataException(
                "The Store sideload lifecycle evidence is invalid.",
                exception);
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }

        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
            actual.Length != expected.Length ||
            expected.Any(name => !actual.Contains(name, StringComparer.Ordinal)))
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

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InvalidDataException Invalid() => new();
}
