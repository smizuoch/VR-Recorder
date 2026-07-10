using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class OpenVrActionManifestPathTests
{
    [Fact]
    public void ResolvesExistingManifestUnderAbsoluteInstallRoot()
    {
        using var directory = TemporaryDirectory.Create(
            "VR Recorder 日本語 install");
        var openVrDirectory = Path.Combine(directory.Path, "OpenVr");
        Directory.CreateDirectory(openVrDirectory);
        var manifestPath = Path.Combine(openVrDirectory, "actions.json");
        File.WriteAllText(manifestPath, "{}");

        var resolved = OpenVrActionManifestPath.Resolve(directory.Path);

        Assert.True(Path.IsPathFullyQualified(resolved));
        Assert.Equal(Path.GetFullPath(manifestPath), resolved);
    }

    [Fact]
    public void RelativeInstallRootIsRejectedBeforeManifestLookup()
    {
        Assert.Throws<ArgumentException>(() =>
            OpenVrActionManifestPath.Resolve("relative-install-root"));
    }

    [Fact]
    public void MissingPackagedManifestIsRejectedBeforeNativeRegistration()
    {
        using var directory = TemporaryDirectory.Create("missing-manifest");

        var exception = Assert.Throws<FileNotFoundException>(() =>
            OpenVrActionManifestPath.Resolve(directory.Path));

        Assert.Equal(
            Path.Combine(directory.Path, "OpenVr", "actions.json"),
            exception.FileName);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create(string suffix)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-openvr-{Guid.NewGuid():N}-{suffix}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
