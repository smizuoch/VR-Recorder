using System.Text.Json;
using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsApplicationPayloadIdentityPublisherTests
{
    [Fact]
    public async Task PublishesCanonicalIdentityOutsideSealedPayload()
    {
        using var fixture = Fixture.Create();

        var result = await WindowsApplicationPayloadIdentityPublisher
            .PublishAsync(
                fixture.Payload,
                fixture.OutputPath,
                CancellationToken.None);

        Assert.True(result.IsPublished);
        Assert.Empty(result.Issues);
        Assert.Equal(fixture.OutputPath, result.IdentityPath);
        var bytes = await File.ReadAllBytesAsync(fixture.OutputPath);
        Assert.False(bytes.AsSpan().StartsWith(
            new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\r', bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("0.1.0", root.GetProperty("productVersion").GetString());
        Assert.Equal(
            Fixture.SourceRevision,
            root.GetProperty("sourceRevision").GetString());
        Assert.Equal("win-x64", root.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal(
            "VRRecorder.App.exe",
            root.GetProperty("entryPoint").GetString());
        Assert.Equal(Fixture.ShaA, root.GetProperty(
            "applicationExecutableSha256").GetString());
        Assert.Equal(Fixture.ShaB, root.GetProperty(
            "payloadInventorySha256").GetString());
        Assert.Equal("legal-id", root.GetProperty("legalBundleId").GetString());
        Assert.Equal(Fixture.ShaC, root.GetProperty(
            "legalManifestSha256").GetString());
        var files = root.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(2, files.Length);
        Assert.Equal("a.dll", files[0].GetProperty("path").GetString());
        Assert.Equal("z.json", files[1].GetProperty("path").GetString());
    }

    [Fact]
    public async Task ExistingOutputAndOutputInsidePayloadAreRejected()
    {
        using var fixture = Fixture.Create();
        await File.WriteAllTextAsync(fixture.OutputPath, "existing");
        AssertIssue(
            "application-payload-identity-output-exists",
            await WindowsApplicationPayloadIdentityPublisher.PublishAsync(
                fixture.Payload,
                fixture.OutputPath,
                CancellationToken.None));
        AssertIssue(
            "application-payload-identity-output-overlaps-payload",
            await WindowsApplicationPayloadIdentityPublisher.PublishAsync(
                fixture.Payload,
                Path.Combine(fixture.Payload.RootDirectory, "identity.json"),
                CancellationToken.None));
    }

    private static void AssertIssue(
        string code,
        WindowsApplicationPayloadIdentityPublication result)
    {
        Assert.False(result.IsPublished);
        Assert.Null(result.IdentityPath);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    private sealed class Fixture : IDisposable
    {
        public const string SourceRevision =
            "0123456789abcdef0123456789abcdef01234567";
        public const string ShaA =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string ShaB =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public const string ShaC =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        private Fixture(string root)
        {
            Root = root;
            var publishRoot = Path.Combine(root, "publish");
            Directory.CreateDirectory(publishRoot);
            OutputPath = Path.Combine(root, "application-payload-identity.json");
            var inventory = new WindowsPublishDirectoryInventory(
                publishRoot,
                "VRRecorder.App.exe",
                ShaA,
                ShaB,
                [
                    new StagedPayloadFile(
                        "z.json",
                        ShaC,
                        12,
                        StagedArtifactKind.Asset),
                    new StagedPayloadFile(
                        "a.dll",
                        ShaA,
                        34,
                        StagedArtifactKind.NativeLibrary),
                ]);
            Payload = new SealedWindowsApplicationPayload(
                inventory,
                new ManagedApplicationBuildIdentity(
                    "0.1.0",
                    SourceRevision,
                    "win-x64"),
                "win-x64",
                "legal-id",
                ShaC);
        }

        public string Root { get; }

        public string OutputPath { get; }

        public SealedWindowsApplicationPayload Payload { get; }

        public static Fixture Create()
        {
            var root = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "vrrecorder-identity-publisher-tests",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            return new Fixture(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
