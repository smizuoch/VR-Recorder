using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;
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
        ((IRecordingMediaEventSink)sink).Publish(new RecordingMediaProfile(
            SourceWidth: 1920,
            SourceHeight: 1080,
            SourcePixelFormat: VideoPixelFormat.Bgra8,
            EstimatedSourceFramesPerSecond: 59.94,
            OutputWidth: 1920,
            OutputHeight: 1080,
            OutputFramesPerSecond: 60,
            Encoder: EncoderKind.Nvenc,
            GpuVendor: GpuVendor.Nvidia));
        ((IRecordingMediaEventSink)sink).Publish(
            new RecordingSessionStatistics(
                SourceVideoFrameCount: 120,
                MuxedVideoPacketCount: 90,
                MuxedAudioPacketCount: 142,
                DroppedSourceVideoFrameCount: 30,
                DuplicatedOutputVideoFrameCount: 4,
                LatestEncodeLatency: TimeSpan.FromMicroseconds(2_400),
                MaximumEncodeLatency: TimeSpan.FromMicroseconds(8_000),
                AudioVideoOffset: TimeSpan.FromMicroseconds(-15_000)));
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
        Assert.Equal(7, lines.Length);
        Assert.Contains("\"event\":\"recording.storage\"", lines[0]);
        Assert.Contains("\"availableBytes\":\"123456789\"", lines[0]);
        Assert.Contains("\"state\":\"warning\"", lines[0]);
        Assert.Contains("\"estimatedRemainingSeconds\":\"42\"", lines[0]);
        Assert.Contains("\"event\":\"recording.saved\"", lines[1]);
        Assert.Contains("\"container\":\"mp4\"", lines[1]);
        Assert.Contains("\"event\":\"camera.restore_warning\"", lines[2]);
        Assert.Contains("\"reason\":\"recording_completed\"", lines[2]);
        Assert.Contains("\"failureType\":\"IOException\"", lines[2]);
        Assert.Contains("\"event\":\"recording.media_profile\"", lines[3]);
        Assert.Contains("\"sourceWidth\":\"1920\"", lines[3]);
        Assert.Contains("\"outputFramesPerSecond\":\"60\"", lines[3]);
        Assert.Contains("\"estimatedSourceFramesPerSecond\":\"59.94\"", lines[3]);
        Assert.Contains("\"sourcePixelFormat\":\"bgra8\"", lines[3]);
        Assert.Contains("\"encoder\":\"nvenc\"", lines[3]);
        Assert.Contains("\"gpuVendor\":\"nvidia\"", lines[3]);
        Assert.Contains(
            "\"event\":\"recording.media_statistics\"",
            lines[4]);
        Assert.Contains("\"sourceVideoFrameCount\":\"120\"", lines[4]);
        Assert.Contains("\"droppedSourceVideoFrameCount\":\"30\"", lines[4]);
        Assert.Contains("\"duplicatedOutputVideoFrameCount\":\"4\"", lines[4]);
        Assert.Contains("\"latestEncodeLatencyMicroseconds\":\"2400\"", lines[4]);
        Assert.Contains("\"maximumEncodeLatencyMicroseconds\":\"8000\"", lines[4]);
        Assert.Contains("\"audioVideoOffsetMicroseconds\":\"-15000\"", lines[4]);
        Assert.Contains("\"event\":\"audio.input_warning\"", lines[5]);
        Assert.Contains("\"kind\":\"input_unavailable\"", lines[5]);
        Assert.Contains("\"input\":\"microphone\"", lines[5]);
        Assert.Contains("\"framePosition\":\"4800\"", lines[5]);
        Assert.Contains("\"failureType\":\"IOException\"", lines[5]);
        Assert.Contains("\"event\":\"audio.input_status\"", lines[6]);
        Assert.Contains("\"kind\":\"input_recovered\"", lines[6]);
        Assert.Contains("\"framePosition\":\"9600\"", lines[6]);
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
    public async Task RecordsApplicationEnvironmentMetadata()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(directory.Path);
        using var sink = new StructuredRecordingEventSink(
            log,
            new FixedWallClock(DateTimeOffset.UnixEpoch));
        var snapshot = new RecordingEnvironmentSnapshot(
            "0.3.0",
            "10.0.26100",
            RecordingProcessArchitecture.X64,
            "ven_10de&dev_2684",
            GpuVendor.Nvidia,
            "32.0.15.6094");

        ((IRecordingMediaEventSink)sink).Publish(snapshot);
        sink.Dispose();

        var content = await File.ReadAllTextAsync(Path.Combine(
            directory.Path,
            "vr-recorder.jsonl"));
        Assert.Contains("\"event\":\"application.environment\"", content);
        Assert.Contains("\"appVersion\":\"0.3.0\"", content);
        Assert.Contains("\"architecture\":\"x64\"", content);
        Assert.Contains("\"gpuModel\":\"ven_10de\\u0026dev_2684\"", content);
        Assert.Contains("\"gpuVendor\":\"nvidia\"", content);
        Assert.Contains("\"osBuild\":\"10.0.26100\"", content);
        Assert.Contains("\"driverVersion\":\"32.0.15.6094\"", content);
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
