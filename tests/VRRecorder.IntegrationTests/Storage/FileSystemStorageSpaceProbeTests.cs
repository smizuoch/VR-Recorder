using VRRecorder.Domain.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class FileSystemStorageSpaceProbeTests
{
    [Fact]
    public async Task MeasuresTheFilesystemContainingSelectedOutputDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var outputPath = new OutputPath(directory.Path);
        var root = Path.GetPathRoot(directory.Path) ??
                   throw new InvalidOperationException(
                       "The temporary directory has no filesystem root.");
        var expected = new DriveInfo(root).AvailableFreeSpace;
        var probe = new FileSystemStorageSpaceProbe();

        var measured = await probe.MeasureAsync(
            outputPath,
            CancellationToken.None);

        Assert.InRange(
            measured.AvailableBytes,
            Math.Max(0, expected - 16 * 1024 * 1024),
            expected + 16 * 1024 * 1024);
    }

    [Fact]
    public async Task MissingOutputDirectoryIsRejected()
    {
        var missingPath = new OutputPath(Path.Combine(
            Path.GetTempPath(),
            $"vr-recorder-missing-{Guid.NewGuid():N}"));
        var probe = new FileSystemStorageSpaceProbe();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => probe.MeasureAsync(missingPath, CancellationToken.None));
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
                $"vr-recorder-space-tests-{Guid.NewGuid():N}");
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
