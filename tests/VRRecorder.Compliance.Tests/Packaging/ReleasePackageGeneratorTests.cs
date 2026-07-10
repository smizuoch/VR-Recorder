using VRRecorder.Compliance.Packaging;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Packaging;

public sealed class ReleasePackageGeneratorTests
{
    [Fact]
    public async Task UnregisteredNativeDllInFinalStagingBlocksPackageWriter()
    {
        using var directory = TemporaryDirectory.Create();
        var stagingPath = Path.Combine(directory.Path, "staging");
        var nativeDirectory = Path.Combine(stagingPath, "native");
        Directory.CreateDirectory(nativeDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(nativeDirectory, "rogue.dll"),
            [0x4d, 0x5a]);
        var packagePath = Path.Combine(directory.Path, "VR-Recorder.zip");
        var writer = new SpyReleasePackageWriter();
        var generator = new ReleasePackageGenerator(
            new FileSystemStagingInventoryReader(),
            writer);
        var request = new ReleasePackageRequest(
            stagingPath,
            packagePath,
            []);

        var result = await generator.GenerateAsync(
            request,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("unregistered-staging-file", issue.Code);
        Assert.Equal("native/rogue.dll", issue.Subject);
        Assert.Equal(0, writer.CallCount);
        Assert.False(File.Exists(packagePath));
    }

    private sealed class SpyReleasePackageWriter : IReleasePackageWriter
    {
        public int CallCount { get; private set; }

        public async Task WriteAsync(
            string packagePath,
            StagingInventory inventory,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await File.WriteAllTextAsync(
                packagePath,
                $"files={inventory.Files.Count}",
                cancellationToken);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-tests-{Guid.NewGuid():N}");
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
