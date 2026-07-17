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

    private static void AssertInvalid(string json) =>
        Assert.Throws<InvalidDataException>(() =>
            WindowsApplicationPayloadIdentityReader.Read(
                Encoding.UTF8.GetBytes(json)));
}
