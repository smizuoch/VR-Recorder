using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class NativeFactorySelectionEvidenceValidatorTests
{
    private const string IntentSha =
        "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
    private const string OtherSha =
        "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
    private const string MarkerPrefix = "VRRECORDER_FACTORY_SELECTION_V1:";

    [Fact]
    public void ExactFullProductionEvidenceIsAccepted()
    {
        var binary = Binary(IntentSha);
        var evidence = Evidence(binary);

        var issues = Validate(evidence, binary);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData(
        "\"evidenceKind\":\"linked-native-factory-selection\"",
        "\"evidenceKind\":\"other\"")]
    [InlineData("\"fullProductionRequired\":true", "\"fullProductionRequired\":false")]
    [InlineData(
        "\"variant\":\"PRODUCTION\"",
        "\"variant\":\"UNAVAILABLE\"")]
    [InlineData(
        "\"source\":\"production_media_backend.cpp\"",
        "\"source\":\"unavailable_media_backend.cpp\"")]
    [InlineData(
        "\"source\":\"production_encoder_probe_backend.cpp\"",
        "\"source\":\"production_media_backend.cpp\"")]
    [InlineData(
        "\"source\":\"spout2_source_backend.cpp\"",
        "\"source\":\"unavailable_spout_source_backend.cpp\"")]
    [InlineData(
        "\"source\":\"openvr_steamvr_input_backend.cpp\"",
        "\"source\":\"unavailable_steamvr_input_backend.cpp\"")]
    [InlineData(
        "\"file\":\"vrrecorder_native.dll\"",
        "\"file\":\"renamed.dll\"")]
    public void NonProductionOrMismatchedEvidenceIsRejected(
        string oldValue,
        string newValue)
    {
        var binary = Binary(IntentSha);
        var evidence = Evidence(binary).Replace(
            oldValue,
            newValue,
            StringComparison.Ordinal);

        AssertIssue(
            "invalid-native-factory-selection-evidence",
            Validate(evidence, binary));
    }

    [Fact]
    public void UnknownOrDuplicatePropertyIsRejected()
    {
        var binary = Binary(IntentSha);
        var exact = Evidence(binary);
        var unknown = exact.Replace(
            "{\"schemaVersion\":1,",
            "{\"schemaVersion\":1,\"unexpected\":true,",
            StringComparison.Ordinal);
        var duplicate = exact.Replace(
            "{\"schemaVersion\":1,",
            "{\"schemaVersion\":1,\"schemaVersion\":1,",
            StringComparison.Ordinal);
        var nestedUnknown = exact.Replace(
            "\"variant\":\"PRODUCTION\",\"source\":\"production_media_backend.cpp\"",
            "\"variant\":\"PRODUCTION\",\"source\":\"production_media_backend.cpp\",\"unexpected\":true",
            StringComparison.Ordinal);

        AssertIssue(
            "invalid-native-factory-selection-evidence",
            Validate(unknown, binary));
        AssertIssue(
            "invalid-native-factory-selection-evidence",
            Validate(duplicate, binary));
        AssertIssue(
            "invalid-native-factory-selection-evidence",
            Validate(nestedUnknown, binary));
    }

    [Fact]
    public void EvidenceManifestHashMismatchIsRejected()
    {
        var binary = Binary(IntentSha);
        var evidence = Evidence(binary);

        var issues = NativeFactorySelectionEvidenceValidator.Validate(
            Encoding.UTF8.GetBytes(evidence),
            OtherSha,
            "vrrecorder_native.dll",
            binary);

        AssertIssue("native-factory-evidence-hash-mismatch", issues);
    }

    [Fact]
    public void NativeBinaryLengthMismatchIsRejected()
    {
        var binary = Binary(IntentSha);
        var evidence = Evidence(binary).Replace(
            $"\"length\":{binary.Length}",
            $"\"length\":{binary.Length + 1}",
            StringComparison.Ordinal);

        AssertIssue(
            "native-factory-binary-identity-mismatch",
            Validate(evidence, binary));
    }

    [Fact]
    public void NativeBinaryHashMismatchIsRejected()
    {
        var binary = Binary(IntentSha);
        var evidence = Evidence(binary).Replace(
            Sha256(binary),
            OtherSha,
            StringComparison.Ordinal);

        AssertIssue(
            "native-factory-binary-identity-mismatch",
            Validate(evidence, binary));
    }

    [Fact]
    public void NonCanonicalSelectionIntentHashIsRejected()
    {
        var binary = Binary(IntentSha);
        var evidence = Evidence(binary).Replace(
            IntentSha,
            IntentSha.ToUpperInvariant(),
            StringComparison.Ordinal);

        AssertIssue(
            "invalid-native-factory-selection-evidence",
            Validate(evidence, binary));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("different")]
    [InlineData("duplicate")]
    public void NativeBinaryMustContainExactlyOneMatchingMarker(string mode)
    {
        var exact = Binary(IntentSha);
        var binary = mode switch
        {
            "missing" => Encoding.ASCII.GetBytes("plain-native-binary"),
            "different" => Binary(OtherSha),
            "duplicate" => [.. exact, .. exact],
            _ => throw new InvalidOperationException(),
        };
        var evidence = Evidence(binary).Replace(
            mode == "different" ? OtherSha : IntentSha,
            IntentSha,
            StringComparison.Ordinal);

        AssertIssue(
            "native-factory-selection-marker-mismatch",
            Validate(evidence, binary));
    }

    [Fact]
    public void UnexpectedAdditionalFactoryMarkerIsRejected()
    {
        var binary =
            new byte[Binary(IntentSha).Length + Binary(OtherSha).Length];
        Binary(IntentSha).CopyTo(binary, 0);
        Binary(OtherSha).CopyTo(binary, Binary(IntentSha).Length);
        var evidence = Evidence(binary);

        AssertIssue(
            "native-factory-selection-marker-mismatch",
            Validate(evidence, binary));
    }

    private static IReadOnlyList<ComplianceIssue> Validate(
        string evidence,
        byte[] binary)
    {
        var bytes = Encoding.UTF8.GetBytes(evidence);
        return NativeFactorySelectionEvidenceValidator.Validate(
            bytes,
            Sha256(bytes),
            "vrrecorder_native.dll",
            binary);
    }

    private static byte[] Binary(string intentSha) =>
        Encoding.ASCII.GetBytes($"prefix-{MarkerPrefix}{intentSha}-suffix");

    private static string Evidence(byte[] binary) =>
        $$$"""
        {"schemaVersion":1,"evidenceKind":"linked-native-factory-selection","selectionIntentSha256":"{{{IntentSha}}}","fullProductionRequired":true,"nativeBinary":{"file":"vrrecorder_native.dll","length":{{{binary.Length}}},"sha256":"{{{Sha256(binary)}}}"},"media":{"variant":"PRODUCTION","source":"production_media_backend.cpp"},"encoderProbe":{"variant":"PRODUCTION","source":"production_encoder_probe_backend.cpp"},"spout":{"variant":"PRODUCTION","source":"spout2_source_backend.cpp"},"steamVr":{"variant":"PRODUCTION","source":"openvr_steamvr_input_backend.cpp"}}
        """;

    private static string Sha256(byte[] bytes) => Convert
        .ToHexString(SHA256.HashData(bytes))
        .ToLowerInvariant();

    private static void AssertIssue(
        string code,
        IReadOnlyList<ComplianceIssue> issues)
    {
        var issue = Assert.Single(issues);
        Assert.Equal(code, issue.Code);
    }
}
