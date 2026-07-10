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
        var store = new JsonFileRecordingRightsAcknowledgementStore(
            path,
            new FixedWallClock(DateTimeOffset.UnixEpoch));

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
            """ + Environment.NewLine,
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
