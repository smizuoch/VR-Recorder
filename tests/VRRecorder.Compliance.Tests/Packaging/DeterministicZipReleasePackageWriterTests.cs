using System.IO.Compression;
using System.Security.Cryptography;
using VRRecorder.Compliance.Packaging;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Packaging;

public sealed class DeterministicZipReleasePackageWriterTests
{
    [Fact]
    public async Task RepeatedBuildsAreByteIdenticalCanonicalRealZipArchives()
    {
        using var directory = TemporaryDirectory.Create();
        var stagingPath = Path.Combine(directory.Path, "staging");
        var nestedPath = Path.Combine(stagingPath, "nested");
        var outputPath = Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(nestedPath);
        Directory.CreateDirectory(outputPath);
        var alphaPath = Path.Combine(nestedPath, "alpha.bin");
        var zetaPath = Path.Combine(stagingPath, "zeta.txt");
        byte[] alpha = [0x00, 0x01, 0x02, 0xff];
        var zeta = "deterministic release payload\n"u8.ToArray();
        await File.WriteAllBytesAsync(alphaPath, alpha);
        await File.WriteAllBytesAsync(zetaPath, zeta);
        File.SetLastWriteTimeUtc(alphaPath, new DateTime(2020, 1, 2));
        File.SetLastWriteTimeUtc(zetaPath, new DateTime(2021, 2, 3));
        var scanned = await new FileSystemStagingInventoryReader().ReadAsync(
            stagingPath,
            CancellationToken.None);
        var inventory = scanned with
        {
            Files = scanned.Files
                .Select(file => file.RelativePath == "nested/alpha.bin"
                    ? file with { RelativePath = "nested\\alpha.bin" }
                    : file)
                .Reverse()
                .ToArray(),
        };
        var firstPackage = Path.Combine(outputPath, "first.zip");
        var secondPackage = Path.Combine(outputPath, "second.zip");
        var writer = new DeterministicZipReleasePackageWriter();

        await writer.WriteAsync(
            firstPackage,
            stagingPath,
            inventory,
            CancellationToken.None);
        File.SetLastWriteTimeUtc(alphaPath, new DateTime(2030, 3, 4));
        File.SetLastWriteTimeUtc(zetaPath, new DateTime(2031, 4, 5));
        await writer.WriteAsync(
            secondPackage,
            stagingPath,
            inventory,
            CancellationToken.None);

        var firstBytes = await File.ReadAllBytesAsync(firstPackage);
        var secondBytes = await File.ReadAllBytesAsync(secondPackage);
        Assert.Equal(firstBytes, secondBytes);
        using var archive = new ZipArchive(
            new MemoryStream(firstBytes),
            ZipArchiveMode.Read);
        Assert.Equal(
            ["nested/alpha.bin", "zeta.txt"],
            archive.Entries.Select(entry => entry.FullName));
        Assert.Equal(2, archive.Entries.Select(entry => entry.FullName).Distinct(
            StringComparer.OrdinalIgnoreCase).Count());
        var timestamp = new DateTime(
            1980,
            1,
            1,
            0,
            0,
            0);
        Assert.All(archive.Entries, entry => Assert.Equal(
            timestamp,
            entry.LastWriteTime.DateTime));
        Assert.Equal(alpha, ReadEntry(archive, "nested/alpha.bin"));
        Assert.Equal(zeta, ReadEntry(archive, "zeta.txt"));
    }

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("/absolute.bin")]
    [InlineData("C:/drive.bin")]
    [InlineData("nested/./file.bin")]
    [InlineData("nested//file.bin")]
    public async Task UnsafeArchivePathIsRejectedWithoutPublishing(
        string relativePath)
    {
        using var directory = TemporaryDirectory.Create();
        var stagingPath = Path.Combine(directory.Path, "staging");
        var outputPath = Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(stagingPath);
        Directory.CreateDirectory(outputPath);
        var packagePath = Path.Combine(outputPath, "release.zip");
        var inventory = new StagingInventory(
            [new StagedPayloadFile(
                relativePath,
                new string('0', 64),
                Length: 0,
                StagedArtifactKind.Asset)],
            []);
        var writer = new DeterministicZipReleasePackageWriter();

        await Assert.ThrowsAsync<InvalidDataException>(() => writer.WriteAsync(
            packagePath,
            stagingPath,
            inventory,
            CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outputPath));
    }

    [Fact]
    public async Task HashMismatchLeavesNoPackageOrTemporaryOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var stagingPath = Path.Combine(directory.Path, "staging");
        var outputPath = Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(stagingPath);
        Directory.CreateDirectory(outputPath);
        await File.WriteAllTextAsync(
            Path.Combine(stagingPath, "payload.txt"),
            "payload");
        var scanned = await new FileSystemStagingInventoryReader().ReadAsync(
            stagingPath,
            CancellationToken.None);
        var inventory = scanned with
        {
            Files = [scanned.Files[0] with { Sha256 = new string('0', 64) }],
        };
        var writer = new DeterministicZipReleasePackageWriter();

        await Assert.ThrowsAsync<InvalidDataException>(() => writer.WriteAsync(
            Path.Combine(outputPath, "release.zip"),
            stagingPath,
            inventory,
            CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outputPath));
    }

    [Fact]
    public async Task ExistingPackageIsNotOverwritten()
    {
        using var directory = TemporaryDirectory.Create();
        var stagingPath = Path.Combine(directory.Path, "staging");
        var outputPath = Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(stagingPath);
        Directory.CreateDirectory(outputPath);
        await File.WriteAllTextAsync(
            Path.Combine(stagingPath, "payload.txt"),
            "payload");
        var inventory = await new FileSystemStagingInventoryReader().ReadAsync(
            stagingPath,
            CancellationToken.None);
        var packagePath = Path.Combine(outputPath, "release.zip");
        byte[] original = [0x01, 0x02, 0x03];
        await File.WriteAllBytesAsync(packagePath, original);
        var writer = new DeterministicZipReleasePackageWriter();

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(
            packagePath,
            stagingPath,
            inventory,
            CancellationToken.None));

        Assert.Equal(original, await File.ReadAllBytesAsync(packagePath));
        Assert.Single(Directory.EnumerateFileSystemEntries(outputPath));
    }

    [Fact]
    public async Task StagingSymbolicLinkIsRejectedWithoutPublishing()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var stagingPath = Path.Combine(directory.Path, "staging");
        var outputPath = Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(stagingPath);
        Directory.CreateDirectory(outputPath);
        var targetPath = Path.Combine(directory.Path, "target.bin");
        byte[] content = [0x10, 0x20, 0x30];
        await File.WriteAllBytesAsync(targetPath, content);
        File.CreateSymbolicLink(
            Path.Combine(stagingPath, "linked.bin"),
            targetPath);
        var inventory = new StagingInventory(
            [new StagedPayloadFile(
                "linked.bin",
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                content.Length,
                StagedArtifactKind.Asset)],
            []);
        var writer = new DeterministicZipReleasePackageWriter();

        await Assert.ThrowsAsync<InvalidDataException>(() => writer.WriteAsync(
            Path.Combine(outputPath, "release.zip"),
            stagingPath,
            inventory,
            CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outputPath));
    }

    private static byte[] ReadEntry(ZipArchive archive, string path)
    {
        var entry = Assert.Single(archive.Entries, candidate =>
            candidate.FullName == path);
        using var source = entry.Open();
        using var content = new MemoryStream();
        source.CopyTo(content);
        return content.ToArray();
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
                $"vr-recorder-package-writer-tests-{Guid.NewGuid():N}");
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
