using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
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
        using var sink = new StructuredRecordingEventSink(
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
        ((IAudioSessionEventSink)sink).Publish(new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            AudioInput.Microphone,
            FramePosition: 4_800,
            new IOException("secret microphone endpoint")));
        ((IAudioSessionEventSink)sink).Publish(new AudioSessionStatus(
            AudioSessionStatusKind.InputRecovered,
            AudioInput.Microphone,
            FramePosition: 9_600));
        sink.Dispose();

        var content = await File.ReadAllTextAsync(Path.Combine(
            directory.Path,
            "vr-recorder.jsonl"));
        Assert.DoesNotContain(directory.Path, content, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", content, StringComparison.Ordinal);
        Assert.DoesNotContain("secret avatar", content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret microphone",
            content,
            StringComparison.Ordinal);
        var lines = content.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.Contains("\"event\":\"recording.storage\"", lines[0]);
        Assert.Contains("\"availableBytes\":\"123456789\"", lines[0]);
        Assert.Contains("\"state\":\"warning\"", lines[0]);
        Assert.Contains("\"estimatedRemainingSeconds\":\"42\"", lines[0]);
        Assert.Contains("\"event\":\"recording.saved\"", lines[1]);
        Assert.Contains("\"container\":\"mp4\"", lines[1]);
        Assert.Contains("\"event\":\"camera.restore_warning\"", lines[2]);
        Assert.Contains("\"reason\":\"recording_completed\"", lines[2]);
        Assert.Contains("\"failureType\":\"IOException\"", lines[2]);
        Assert.Contains("\"event\":\"audio.input_warning\"", lines[3]);
        Assert.Contains("\"kind\":\"input_unavailable\"", lines[3]);
        Assert.Contains("\"input\":\"microphone\"", lines[3]);
        Assert.Contains("\"framePosition\":\"4800\"", lines[3]);
        Assert.Contains("\"failureType\":\"IOException\"", lines[3]);
        Assert.Contains("\"event\":\"audio.input_status\"", lines[4]);
        Assert.Contains("\"kind\":\"input_recovered\"", lines[4]);
        Assert.Contains("\"framePosition\":\"9600\"", lines[4]);
    }

    [Fact]
    public async Task AudioPublishDoesNotWaitAndCapturesCallbackTimestamp()
    {
        var callbackTime = new DateTimeOffset(
            2026,
            7,
            11,
            1,
            2,
            3,
            TimeSpan.Zero);
        var clock = new MutableWallClock(callbackTime);
        var writer = new BlockingDiagnosticLogWriter();
        using var sink = new StructuredRecordingEventSink(
            writer,
            clock,
            audioQueueCapacity: 2);
        var warning = new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            AudioInput.Desktop,
            FramePosition: 4_800);

        var publishing = Task.Run(() =>
            ((IAudioSessionEventSink)sink).Publish(warning));
        try
        {
            await writer.FirstWriteStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            await publishing.WaitAsync(TimeSpan.FromSeconds(5));
            clock.LocalNow = callbackTime.AddMinutes(10);
        }
        finally
        {
            writer.ReleaseFirstWrite.TrySetResult();
        }

        sink.Dispose();

        var entry = Assert.Single(writer.Entries);
        Assert.Equal(callbackTime, entry.TimestampUtc);
        Assert.Equal("audio.input_warning", entry.EventName);
    }

    [Fact]
    public void WriterFailureDoesNotStopQueuedAudioOrEscapeDispose()
    {
        var writer = new ThrowingFirstDiagnosticLogWriter();
        var sink = new StructuredRecordingEventSink(
            writer,
            new FixedWallClock(DateTimeOffset.UnixEpoch));

        ((IAudioSessionEventSink)sink).Publish(new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            AudioInput.Desktop,
            FramePosition: 4_800));
        ((IAudioSessionEventSink)sink).Publish(new AudioSessionStatus(
            AudioSessionStatusKind.InputRecovered,
            AudioInput.Desktop,
            FramePosition: 9_600));

        sink.Dispose();
        sink.Dispose();
        ((IAudioSessionEventSink)sink).Publish(new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            AudioInput.Microphone,
            FramePosition: 14_400));

        Assert.Equal(2, writer.Entries.Count);
        Assert.Equal("audio.input_warning", writer.Entries[0].EventName);
        Assert.Equal("audio.input_status", writer.Entries[1].EventName);
    }

    private sealed class FixedWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = localNow;
    }

    private sealed class MutableWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; set; } = localNow;
    }

    private sealed class BlockingDiagnosticLogWriter : IDiagnosticLogWriter
    {
        private readonly Lock _gate = new();

        public List<DiagnosticLogEntry> Entries { get; } = [];

        public TaskCompletionSource FirstWriteStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstWrite { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WriteAsync(
            DiagnosticLogEntry entry,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Entries.Add(entry);
            }

            FirstWriteStarted.TrySetResult();
            await ReleaseFirstWrite.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingFirstDiagnosticLogWriter
        : IDiagnosticLogWriter
    {
        private int _writeCount;

        public List<DiagnosticLogEntry> Entries { get; } = [];

        public Task WriteAsync(
            DiagnosticLogEntry entry,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(entry);
            return Interlocked.Increment(ref _writeCount) == 1
                ? Task.FromException(new IOException(
                    "diagnostic storage unavailable"))
                : Task.CompletedTask;
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
