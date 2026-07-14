using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Staging;

internal static class NativeFactorySelectionEvidenceValidator
{
    private const int MaximumEvidenceBytes = 1024 * 1024;
    private const string EvidenceKind = "linked-native-factory-selection";
    private const string MarkerPrefix = "VRRECORDER_FACTORY_SELECTION_V1:";
    private static readonly byte[] MarkerPrefixBytes =
        Encoding.ASCII.GetBytes(MarkerPrefix);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "evidenceKind",
        "selectionIntentSha256",
        "fullProductionRequired",
        "nativeBinary",
        "media",
        "encoderProbe",
        "spout",
        "steamVr",
    ];
    private static readonly string[] NativeBinaryProperties =
    [
        "file",
        "length",
        "sha256",
    ];
    private static readonly string[] FamilyProperties =
    [
        "variant",
        "source",
    ];

    public static IReadOnlyList<ComplianceIssue> Validate(
        byte[] utf8Evidence,
        string expectedEvidenceSha256,
        string expectedNativeBinaryFileName,
        byte[] nativeBinary)
    {
        ArgumentNullException.ThrowIfNull(utf8Evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEvidenceSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            expectedNativeBinaryFileName);
        ArgumentNullException.ThrowIfNull(nativeBinary);

        var actualEvidenceHash = Convert
            .ToHexString(SHA256.HashData(utf8Evidence))
            .ToLowerInvariant();
        if (!IsLowerHexSha256(expectedEvidenceSha256) ||
            !string.Equals(
                actualEvidenceHash,
                expectedEvidenceSha256,
                StringComparison.Ordinal))
        {
            return
            [
                new ComplianceIssue(
                    "native-factory-evidence-hash-mismatch",
                    expectedNativeBinaryFileName),
            ];
        }

        NativeFactoryEvidence evidence;
        try
        {
            evidence = Parse(utf8Evidence);
        }
        catch (Exception exception) when (
            exception is JsonException or
                DecoderFallbackException or
                InvalidDataException or
                InvalidOperationException or
                KeyNotFoundException or
                ArgumentException)
        {
            return
            [
                new ComplianceIssue(
                    "invalid-native-factory-selection-evidence",
                    expectedNativeBinaryFileName),
            ];
        }

        if (!evidence.FullProductionRequired ||
            !string.Equals(
                evidence.NativeBinary.File,
                expectedNativeBinaryFileName,
                StringComparison.Ordinal) ||
            !IsExactProductionFamily(
                evidence.Media,
                "production_media_backend.cpp") ||
            !IsExactProductionFamily(
                evidence.EncoderProbe,
                "production_encoder_probe_backend.cpp") ||
            !IsExactProductionFamily(
                evidence.Spout,
                "spout2_source_backend.cpp") ||
            !IsExactProductionFamily(
                evidence.SteamVr,
                "openvr_steamvr_input_backend.cpp"))
        {
            return
            [
                new ComplianceIssue(
                    "invalid-native-factory-selection-evidence",
                    expectedNativeBinaryFileName),
            ];
        }

        var actualBinaryHash = Convert
            .ToHexString(SHA256.HashData(nativeBinary))
            .ToLowerInvariant();
        if (evidence.NativeBinary.Length != nativeBinary.LongLength ||
            !string.Equals(
                evidence.NativeBinary.Sha256,
                actualBinaryHash,
                StringComparison.Ordinal))
        {
            return
            [
                new ComplianceIssue(
                    "native-factory-binary-identity-mismatch",
                    expectedNativeBinaryFileName),
            ];
        }

        var markers = ReadFactoryMarkers(nativeBinary);
        if (markers.Count != 1 ||
            !string.Equals(
                markers[0],
                evidence.SelectionIntentSha256,
                StringComparison.Ordinal))
        {
            return
            [
                new ComplianceIssue(
                    "native-factory-selection-marker-mismatch",
                    expectedNativeBinaryFileName),
            ];
        }

        return [];
    }

    private static NativeFactoryEvidence Parse(byte[] utf8Evidence)
    {
        if (utf8Evidence.Length == 0 ||
            utf8Evidence.Length > MaximumEvidenceBytes)
        {
            throw new InvalidDataException();
        }

        using var document = JsonDocument.Parse(
            StrictUtf8.GetString(utf8Evidence),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 6,
            });
        var root = document.RootElement;
        RequireExactProperties(root, RootProperties);
        if (RequiredInt32(root, "schemaVersion") != 1 ||
            !string.Equals(
                RequiredString(root, "evidenceKind"),
                EvidenceKind,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException();
        }

        var selectionIntentSha256 = RequiredString(
            root,
            "selectionIntentSha256");
        if (!IsLowerHexSha256(selectionIntentSha256))
        {
            throw new InvalidDataException();
        }

        var nativeBinary = RequiredObject(root, "nativeBinary");
        RequireExactProperties(nativeBinary, NativeBinaryProperties);
        var binarySha256 = RequiredString(nativeBinary, "sha256");
        var binaryLength = RequiredInt64(nativeBinary, "length");
        if (!IsLowerHexSha256(binarySha256) || binaryLength < 0)
        {
            throw new InvalidDataException();
        }

        return new NativeFactoryEvidence(
            selectionIntentSha256,
            RequiredBoolean(root, "fullProductionRequired"),
            new NativeBinaryEvidence(
                RequiredString(nativeBinary, "file"),
                binaryLength,
                binarySha256),
            ParseFamily(root, "media"),
            ParseFamily(root, "encoderProbe"),
            ParseFamily(root, "spout"),
            ParseFamily(root, "steamVr"));
    }

    private static FactoryFamilyEvidence ParseFamily(
        JsonElement root,
        string propertyName)
    {
        var family = RequiredObject(root, propertyName);
        RequireExactProperties(family, FamilyProperties);
        return new FactoryFamilyEvidence(
            RequiredString(family, "variant"),
            RequiredString(family, "source"));
    }

    private static bool IsExactProductionFamily(
        FactoryFamilyEvidence family,
        string expectedSource) =>
        string.Equals(
            family.Variant,
            "PRODUCTION",
            StringComparison.Ordinal) &&
        string.Equals(
            family.Source,
            expectedSource,
            StringComparison.Ordinal);

    private static List<string?> ReadFactoryMarkers(byte[] binary)
    {
        var markers = new List<string?>();
        var remaining = binary.AsSpan();
        while (true)
        {
            var index = remaining.IndexOf(MarkerPrefixBytes);
            if (index < 0)
            {
                return markers;
            }

            remaining = remaining[(index + MarkerPrefixBytes.Length)..];
            if (remaining.Length < 64)
            {
                markers.Add(null);
                return markers;
            }

            var digest = remaining[..64];
            markers.Add(IsLowerHex(digest)
                ? Encoding.ASCII.GetString(digest)
                : null);
            remaining = remaining[64..];
        }
    }

    private static bool IsLowerHexByte(byte value) =>
        value is >= (byte)'0' and <= (byte)'9' or
            >= (byte)'a' and <= (byte)'f';

    private static bool IsLowerHex(ReadOnlySpan<byte> value)
    {
        foreach (var character in value)
        {
            if (!IsLowerHexByte(character))
            {
                return false;
            }
        }

        return true;
    }

    private static void RequireExactProperties(
        JsonElement element,
        string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException();
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw new InvalidDataException();
            }
        }

        if (actual.Count != required.Length ||
            required.Any(property => !actual.Contains(property)))
        {
            throw new InvalidDataException();
        }
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Object
            ? property
            : throw new InvalidDataException();
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException();
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException()
            : value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException();
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out var value)
            ? value
            : throw new InvalidDataException();
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException(),
        };
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record NativeFactoryEvidence(
        string SelectionIntentSha256,
        bool FullProductionRequired,
        NativeBinaryEvidence NativeBinary,
        FactoryFamilyEvidence Media,
        FactoryFamilyEvidence EncoderProbe,
        FactoryFamilyEvidence Spout,
        FactoryFamilyEvidence SteamVr);

    private sealed record NativeBinaryEvidence(
        string File,
        long Length,
        string Sha256);

    private sealed record FactoryFamilyEvidence(
        string Variant,
        string Source);
}
