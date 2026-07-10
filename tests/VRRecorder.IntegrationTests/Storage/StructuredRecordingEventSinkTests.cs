using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class StructuredRecordingEventSinkTests
{
    [Fact]
    public async Task RecordsOperationalFieldsWithoutPathsOrFailureMessages()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(directory.Path);
        var sink = new StructuredRecordingEventSink(
            log,
            new FixedWallClock(new DateTimeOffset(
                2026,
                7,
                11,
                1,
                2,
                3,
                TimeSpan.Zero)));

        await ((IRecordingStorageStatusSink)sink).PublishAsync(
            new RecordingStorageSnapshot(
                new StorageSpace(123_456_789),
                RecordingStorageState.Warning,
                TimeSpan.FromSeconds(42)),
            CancellationToken.None);
        await ((ISavedRecordingSink)sink).PublishAsync(
            new FinalizedRecording(Path.Combine(
                directory.Path,
                "private-user-world-name.mp4")),
            CancellationToken.None);
        await ((ICameraRestoreWarningSink)sink).PublishAsync(
            new CameraRestoreWarning(
                CameraRestoreWarningReason.RecordingCompleted,
                new IOException("secret avatar and user name")),
            CancellationToken.None);

        var content = await File.ReadAllTextAsync(Path.Combine(
            directory.Path,
            "vr-recorder.jsonl"));
        Assert.DoesNotContain(directory.Path, content, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", content, StringComparison.Ordinal);
        Assert.DoesNotContain("secret avatar", content, StringComparison.Ordinal);
        var lines = content.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\"event\":\"recording.storage\"", lines[0]);
        Assert.Contains("\"availableBytes\":\"123456789\"", lines[0]);
        Assert.Contains("\"state\":\"warning\"", lines[0]);
        Assert.Contains("\"estimatedRemainingSeconds\":\"42\"", lines[0]);
        Assert.Contains("\"event\":\"recording.saved\"", lines[1]);
        Assert.Contains("\"container\":\"mp4\"", lines[1]);
        Assert.Contains("\"event\":\"camera.restore_warning\"", lines[2]);
        Assert.Contains("\"reason\":\"recording_completed\"", lines[2]);
        Assert.Contains("\"failureType\":\"IOException\"", lines[2]);
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
                $"vr-recorder-event-log-tests-{Guid.NewGuid():N}");
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
