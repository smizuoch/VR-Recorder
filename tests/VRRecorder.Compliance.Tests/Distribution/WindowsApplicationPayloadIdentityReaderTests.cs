using System.Text;
using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsApplicationPayloadIdentityReaderTests
{
    private const string ShaA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ShaC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string SourceRevision =
        "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void CanonicalPublisherOutputRoundTripsAndRecomputesInventory()
    {
        var bytes = IdentityBytes();

        var document = WindowsApplicationPayloadIdentityReader.Read(bytes);

        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("0.1.0", document.Payload.ProductVersion);
        Assert.Equal(SourceRevision, document.Payload.SourceRevision);
        Assert.Equal("win-x64", document.Payload.RuntimeIdentifier);
        Assert.Equal(ShaA, document.Payload.ApplicationExecutableSha256);
        Assert.Equal(
            WindowsPublishInventoryDigest.Compute(document.Files),
            document.Payload.PayloadInventorySha256);
        Assert.Equal("legal-id", document.Payload.LegalBundleId);
        Assert.Equal(ShaC, document.Payload.LegalManifestSha256);
        Assert.Equal("VRRecorder.App.exe", document.EntryPoint);
        Assert.Equal(3, document.Files.Count);
        Assert.Matches("^[0-9a-f]{64}$", document.DocumentSha256);
    }

    [Fact]
    public void UnknownVersionPropertyAndDuplicatePropertyAreRejected()
    {
        var json = Encoding.UTF8.GetString(IdentityBytes());

        AssertInvalid(json.Replace(
            "\"schemaVersion\": 1",
            "\"schemaVersion\": 2",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1,\n  \"unknown\": true,",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "\"productVersion\": \"0.1.0\",",
            "\"productVersion\": \"0.1.0\",\n  " +
            "\"productVersion\": \"0.1.0\",",
            StringComparison.Ordinal));
    }

    [Fact]
    public void ForgedInventoryDigestAndWindowsDuplicatePathAreRejected()
    {
        var json = Encoding.UTF8.GetString(IdentityBytes());
        var actualDigest = WindowsApplicationPayloadIdentityReader
            .Read(Encoding.UTF8.GetBytes(json))
            .Payload
            .PayloadInventorySha256;
        AssertInvalid(json.Replace(
            actualDigest,
            new string('d', 64),
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "\"path\": \"z.json\"",
            "\"path\": \"A.dll\"",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"productVersion\": \"0.1.0\"", "\"productVersion\": \"1.2.3-alpha.1+build.7\"")]
    [InlineData("\"sourceRevision\": \"0123456789abcdef0123456789abcdef01234567\"", "\"sourceRevision\": \"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"")]
    [InlineData("\"legalBundleId\": \"legal-id\"", "\"legalBundleId\": \"legal-id!v1\"")]
    public void CanonicalIdentityAlternativesAreAccepted(
        string oldValue,
        string newValue)
    {
        var document = WindowsApplicationPayloadIdentityReader.Read(
            Encoding.UTF8.GetBytes(Replace(oldValue, newValue)));

        Assert.Equal(3, document.Files.Count);
    }

    [Theory]
    [InlineData("\"productVersion\": \"0.1.0\"", "\"productVersion\": \"01.1.0\"")]
    [InlineData("\"productVersion\": \"0.1.0\"", "\"productVersion\": \"1.0\"")]
    [InlineData("\"sourceRevision\": \"0123456789abcdef0123456789abcdef01234567\"", "\"sourceRevision\": \"0123456789abcdef0123456789abcdef0123456\"")]
    [InlineData("\"sourceRevision\": \"0123456789abcdef0123456789abcdef01234567\"", "\"sourceRevision\": \"0123456789abcdef0123456789abcdef0123456G\"")]
    [InlineData("\"runtimeIdentifier\": \"win-x64\"", "\"runtimeIdentifier\": \"win-arm64\"")]
    [InlineData("\"entryPoint\": \"VRRecorder.App.exe\"", "\"entryPoint\": \"Other.exe\"")]
    [InlineData("\"applicationExecutableSha256\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", "\"applicationExecutableSha256\": \"Aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [InlineData("\"payloadInventorySha256\": \"", "\"payloadInventorySha256\": \"G")]
    [InlineData("\"legalBundleId\": \"legal-id\"", "\"legalBundleId\": \"bad id\"")]
    [InlineData("\"legalManifestSha256\": \"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"", "\"legalManifestSha256\": \"ccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"")]
    [InlineData("\"files\": [", "\"files\": {")]
    [InlineData("\"path\": \"a.dll\"", "\"path\": \"bad\\\\path.dll\"")]
    [InlineData("\"length\": 12", "\"length\": -1")]
    [InlineData("\"sha256\": \"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"", "\"sha256\": \"gccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"")]
    [InlineData("\"kind\": \"nativeLibrary\"", "\"kind\": \"unknown\"")]
    [InlineData("\"kind\": \"executable\"", "\"kind\": \"asset\"")]
    [InlineData("\"path\": \"VRRecorder.App.exe\"", "\"path\": \"vrrecorder.app.exe\"")]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": \"1\"")]
    [InlineData("\"productVersion\": \"0.1.0\"", "\"productVersion\": null")]
    [InlineData("\"length\": 12", "\"length\": \"12\"")]
    public void InvalidIdentityBoundariesAreRejected(
        string oldValue,
        string newValue)
    {
        AssertInvalid(Replace(oldValue, newValue));
    }

    [Fact]
    public void EmptyNonUtf8AndOversizedFieldsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsApplicationPayloadIdentityReader.Read([]));
        Assert.Throws<InvalidDataException>(() =>
            WindowsApplicationPayloadIdentityReader.Read([0xff]));
        AssertInvalid(Replace(
            "\"productVersion\": \"0.1.0\"",
            $"\"productVersion\": \"{new string('1', 65)}\""));
        AssertInvalid(Replace(
            "\"legalBundleId\": \"legal-id\"",
            $"\"legalBundleId\": \"{new string('x', 2049)}\""));
    }

    private static byte[] IdentityBytes()
    {
        var files = new StagedPayloadFile[]
        {
            new(
                "a.dll",
                ShaC,
                12,
                StagedArtifactKind.NativeLibrary),
            new(
                "VRRecorder.App.exe",
                ShaA,
                34,
                StagedArtifactKind.Executable),
            new(
                "z.json",
                ShaC,
                56,
                StagedArtifactKind.Asset),
        };
        var inventory = new WindowsPublishDirectoryInventory(
            Path.GetFullPath("publish"),
            "VRRecorder.App.exe",
            ShaA,
            WindowsPublishInventoryDigest.Compute(files),
            files);
        var payload = new SealedWindowsApplicationPayload(
            inventory,
            new ManagedApplicationBuildIdentity(
                "0.1.0",
                SourceRevision,
                "win-x64"),
            "win-x64",
            "legal-id",
            ShaC);
        return WindowsApplicationPayloadIdentityPublisher.Generate(payload);
    }

    private static string Replace(string oldValue, string newValue)
    {
        var json = Encoding.UTF8.GetString(IdentityBytes());
        var replaced = json.Replace(
            oldValue,
            newValue,
            StringComparison.Ordinal);
        Assert.NotEqual(json, replaced);
        return replaced;
    }

    private static void AssertInvalid(string json) =>
        Assert.Throws<InvalidDataException>(() =>
            WindowsApplicationPayloadIdentityReader.Read(
                Encoding.UTF8.GetBytes(json)));
}
