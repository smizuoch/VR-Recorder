using System.Security.Cryptography;
using VRRecorder.Compliance.Packaging;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Packaging;

public sealed class ReleasePackageGeneratorTests
{
    [Fact]
    public void LowLevelGeneratorIsNotAPublicReleaseSurface()
    {
        Assert.False(typeof(ReleasePackageGenerator).IsPublic);
    }

    [Fact]
    public async Task RegisteredNativeDllWithMatchingHashWritesPackageOnce()
    {
        using var directory = TemporaryDirectory.Create();
        var stagingPath = Path.Combine(directory.Path, "staging");
        var nativeDirectory = Path.Combine(stagingPath, "native");
        Directory.CreateDirectory(nativeDirectory);
        byte[] content = [0x4d, 0x5a, 0x01, 0x02];
        await File.WriteAllBytesAsync(
            Path.Combine(nativeDirectory, "capture.dll"),
            content);
        var packagePath = Path.Combine(directory.Path, "VR-Recorder.zip");
        var writer = new SpyReleasePackageWriter();
        var generator = new ReleasePackageGenerator(
            new FileSystemStagingInventoryReader(),
            writer);
        var expectedHash = Convert
            .ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();
        var request = new ReleasePackageRequest(
            stagingPath,
            packagePath,
            [new RegisteredStagedArtifact(
                "capture-native",
                "native/capture.dll",
                expectedHash,
                StagedArtifactKind.NativeLibrary)]);

        var result = await generator.GenerateAsync(
            request,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Issues);
        Assert.Equal(1, writer.CallCount);
        Assert.True(File.Exists(packagePath));
        var stagedFile = Assert.Single(writer.LastInventory!.Files);
        Assert.Equal("native/capture.dll", stagedFile.RelativePath);
        Assert.Equal(expectedHash, stagedFile.Sha256);
    }

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

        public StagingInventory? LastInventory { get; private set; }

        public async Task WriteAsync(
            string packagePath,
            string stagingDirectory,
            StagingInventory inventory,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastInventory = inventory;
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
