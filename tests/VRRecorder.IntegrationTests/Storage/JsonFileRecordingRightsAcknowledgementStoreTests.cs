using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class JsonFileRecordingRightsAcknowledgementStoreTests
{
    [Fact]
    public async Task MissingDocumentIsUnacknowledgedAndSaveRoundTripsAtomically()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "recording-rights.json");
        using var store = new JsonFileRecordingRightsAcknowledgementStore(path);

        Assert.Null(await store.LoadAsync(CancellationToken.None));

        var acknowledgement = new RecordingRightsAcknowledgement(
            RecordingRightsNotice.CurrentVersion,
            new DateTimeOffset(
                2026,
                7,
                11,
                1,
                2,
                3,
                TimeSpan.Zero));
        await store.SaveAsync(acknowledgement, CancellationToken.None);

        Assert.Equal(
            """
            {
              "schemaVersion": 1,
              "noticeVersion": 1,
              "acknowledgedAtUtc": "2026-07-11T01:02:03+00:00"
            }
            """ + "\n",
            await File.ReadAllTextAsync(path));
        Assert.Equal(
            acknowledgement,
            await store.LoadAsync(CancellationToken.None));
        Assert.DoesNotContain(
            Directory.GetFiles(directory.Path),
            file => Path.GetFileName(file).Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidDocumentIsBackedUpAndRequiresAcknowledgementAgain()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "recording-rights.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "noticeVersion": 1,
              "acknowledgedAtUtc": "2026-07-11T01:02:03+00:00",
              "unexpected": "must fail closed"
            }
            """);
        var clock = new FixedWallClock(new DateTimeOffset(
            2026,
            7,
            11,
            4,
            5,
            6,
            789,
            TimeSpan.Zero));
        var store = new JsonFileRecordingRightsAcknowledgementStore(path, clock);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded);
        Assert.False(File.Exists(path));
        var backup = Path.Combine(
            directory.Path,
            "recording-rights.corrupt-20260711T040506789Z.json");
        Assert.True(File.Exists(backup));
        Assert.Contains("unexpected", await File.ReadAllTextAsync(backup));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""
        {
          "schemaVersion": 1,
          "noticeVersion": 1,
          "unexpected": "same property count"
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 2,
          "noticeVersion": 1,
          "acknowledgedAtUtc": "2026-07-11T01:02:03+00:00"
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 1,
          "noticeVersion": 1,
          "acknowledgedAtUtc": null
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 1,
          "noticeVersion": 1,
          "acknowledgedAtUtc": "invalid"
        }
        """)]
    public async Task EveryInvalidDocumentShapeIsBackedUp(string content)
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "recording-rights.json");
        await File.WriteAllTextAsync(path, content);
        using var store = new JsonFileRecordingRightsAcknowledgementStore(
            path,
            new FixedWallClock(DateTimeOffset.UnixEpoch));

        Assert.Null(await store.LoadAsync(CancellationToken.None));

        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(
            directory.Path,
            "recording-rights.corrupt-*.json"));
    }

    [Fact]
    public async Task ExistingCorruptBackupIsPreservedAtNextOrdinal()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "recording-rights.json");
        var firstBackup = Path.Combine(
            directory.Path,
            "recording-rights.corrupt-19700101T000000000Z.json");
        var secondBackup = Path.Combine(
            directory.Path,
            "recording-rights.corrupt-19700101T000000000Z_002.json");
        await File.WriteAllTextAsync(path, "invalid current evidence");
        await File.WriteAllTextAsync(firstBackup, "earlier evidence");
        using var store = new JsonFileRecordingRightsAcknowledgementStore(
            path,
            new FixedWallClock(DateTimeOffset.UnixEpoch));

        Assert.Null(await store.LoadAsync(CancellationToken.None));

        Assert.Equal(
            "earlier evidence",
            await File.ReadAllTextAsync(firstBackup));
        Assert.Equal(
            "invalid current evidence",
            await File.ReadAllTextAsync(secondBackup));
    }

    [Fact]
    public async Task RejectsRelativePathAndUseAfterDisposal()
    {
        Assert.Throws<ArgumentException>(() =>
            new JsonFileRecordingRightsAcknowledgementStore(
                "recording-rights.json"));
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "recording-rights.json");
        var store = new JsonFileRecordingRightsAcknowledgementStore(path);
        var acknowledgement = new RecordingRightsAcknowledgement(
            RecordingRightsNotice.CurrentVersion,
            DateTimeOffset.UnixEpoch);

        store.Dispose();
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.LoadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.SaveAsync(acknowledgement, CancellationToken.None));
    }

    [Fact]
    public async Task DanglingDocumentLinkIsRejectedWithoutReplacingIt()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var target = Path.Combine(directory.Path, "missing.json");
        var link = Path.Combine(directory.Path, "recording-rights.json");
        File.CreateSymbolicLink(link, target);
        using var store = new JsonFileRecordingRightsAcknowledgementStore(link);
        var acknowledgement = new RecordingRightsAcknowledgement(
            RecordingRightsNotice.CurrentVersion,
            DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(
            acknowledgement,
            CancellationToken.None));

        Assert.False(File.Exists(target));
        Assert.Equal(target, new FileInfo(link).LinkTarget);
    }

    [Fact]
    public async Task LinkedDocumentAndParentDirectoryAreRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var targetFile = Path.Combine(directory.Path, "target.json");
        var linkedFile = Path.Combine(directory.Path, "recording-rights.json");
        await File.WriteAllTextAsync(targetFile, "outside evidence");
        File.CreateSymbolicLink(linkedFile, targetFile);
        using (var store = new JsonFileRecordingRightsAcknowledgementStore(
                   linkedFile))
        {
            await Assert.ThrowsAsync<IOException>(() =>
                store.LoadAsync(CancellationToken.None));
        }

        var targetDirectory = Path.Combine(directory.Path, "target-directory");
        var linkedDirectory = Path.Combine(directory.Path, "linked-directory");
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateSymbolicLink(linkedDirectory, targetDirectory);
        using var linkedParentStore =
            new JsonFileRecordingRightsAcknowledgementStore(Path.Combine(
                linkedDirectory,
                "recording-rights.json"));
        var acknowledgement = new RecordingRightsAcknowledgement(
            RecordingRightsNotice.CurrentVersion,
            DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<IOException>(() =>
            linkedParentStore.SaveAsync(
                acknowledgement,
                CancellationToken.None));
        Assert.Equal("outside evidence", await File.ReadAllTextAsync(targetFile));
        Assert.Empty(Directory.EnumerateFileSystemEntries(targetDirectory));
    }

    private sealed class FixedWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = localNow;
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
                $"vr-recorder-rights-tests-{Guid.NewGuid():N}");
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
