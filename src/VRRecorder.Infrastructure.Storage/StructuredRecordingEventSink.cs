using System.Globalization;
using System.Threading.Channels;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Infrastructure.Storage;

public sealed class StructuredRecordingEventSink
    : IRecordingStorageStatusSink,
      ISavedRecordingSink,
      ICameraRestoreWarningSink,
      IAudioSessionEventSink,
      IRecordingMediaEventSink,
      IDisposable
{
    public const int DefaultAudioQueueCapacity = 64;
    private readonly Channel<DiagnosticLogEntry> _queuedEntries;
    private readonly Task _queuedWorker;
    private readonly IWallClock _clock;
    private readonly IDiagnosticLogWriter _log;
    private int _deliveryThreadId;
    private int _disposed;

    public StructuredRecordingEventSink(
        IDiagnosticLogWriter log,
        IWallClock clock,
        int audioQueueCapacity = DefaultAudioQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(audioQueueCapacity);
        _log = log;
        _clock = clock;
        _queuedEntries = Channel.CreateBounded<DiagnosticLogEntry>(
            new BoundedChannelOptions(audioQueueCapacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        _queuedWorker = Task.Run(WriteQueuedEntriesAsync);
    }

    public Task PublishAsync(
        RecordingStorageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return WriteBestEffortAsync(
            new DiagnosticLogEntry(
                TimestampUtc(),
                snapshot.State == Domain.Storage.RecordingStorageState.Healthy
                    ? DiagnosticLogLevel.Information
                    : DiagnosticLogLevel.Warning,
                "recording.storage",
                new Dictionary<string, string>
                {
                    ["availableBytes"] = snapshot.AvailableSpace.AvailableBytes
                        .ToString(CultureInfo.InvariantCulture),
                    ["estimatedRemainingSeconds"] = snapshot.EstimatedRemaining
                        .TotalSeconds
                        .ToString("0.###", CultureInfo.InvariantCulture),
                    ["state"] = StorageStateName(snapshot.State),
                }),
            cancellationToken);
    }

    public Task PublishAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return WriteBestEffortAsync(
            new DiagnosticLogEntry(
                TimestampUtc(),
                DiagnosticLogLevel.Information,
                "recording.saved",
                new Dictionary<string, string>
                {
                    ["container"] = "mp4",
                    ["result"] = "saved",
                }),
            cancellationToken);
    }

    public Task PublishAsync(
        CameraRestoreWarning warning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return WriteBestEffortAsync(
            new DiagnosticLogEntry(
                TimestampUtc(),
                DiagnosticLogLevel.Warning,
                "camera.restore_warning",
                new Dictionary<string, string>
                {
                    ["failureType"] = warning.Failure.GetType().Name,
                    ["reason"] = CameraWarningReasonName(warning.Reason),
                }),
            cancellationToken);
    }

    public void Publish(AudioSessionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        var fields = new Dictionary<string, string>
        {
            ["framePosition"] = warning.FramePosition.ToString(
                CultureInfo.InvariantCulture),
            ["input"] = AudioInputName(warning.Input),
            ["kind"] = AudioWarningKindName(warning.Kind),
        };
        if (warning.Failure is not null)
        {
            fields["failureType"] = warning.Failure.GetType().Name;
        }

        Enqueue(new DiagnosticLogEntry(
            TimestampUtc(),
            DiagnosticLogLevel.Warning,
            "audio.input_warning",
            fields));
    }

    public void Publish(AudioSessionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var fields = new Dictionary<string, string>
        {
            ["framePosition"] = status.FramePosition.ToString(
                CultureInfo.InvariantCulture),
            ["input"] = AudioInputName(status.Input),
            ["kind"] = AudioStatusKindName(status.Kind),
        };
        if (status.RediscoveryBudget is { } budget)
        {
            fields["rediscoveryBudgetMilliseconds"] =
                budget.TotalMilliseconds.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);
        }

        Enqueue(new DiagnosticLogEntry(
            TimestampUtc(),
            DiagnosticLogLevel.Information,
            "audio.input_status",
            fields));
    }

    public void Publish(RecordingMediaProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Enqueue(new DiagnosticLogEntry(
            TimestampUtc(),
            DiagnosticLogLevel.Information,
            "recording.media_profile",
            new Dictionary<string, string>
            {
                ["encoder"] = EncoderName(profile.Encoder),
                ["estimatedSourceFramesPerSecond"] =
                    profile.EstimatedSourceFramesPerSecond.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                ["gpuVendor"] = GpuVendorName(profile.GpuVendor),
                ["outputFramesPerSecond"] = profile.OutputFramesPerSecond
                    .ToString(CultureInfo.InvariantCulture),
                ["outputHeight"] = profile.OutputHeight.ToString(
                    CultureInfo.InvariantCulture),
                ["outputWidth"] = profile.OutputWidth.ToString(
                    CultureInfo.InvariantCulture),
                ["sourceHeight"] = profile.SourceHeight.ToString(
                    CultureInfo.InvariantCulture),
                ["sourcePixelFormat"] = PixelFormatName(
                    profile.SourcePixelFormat),
                ["sourceWidth"] = profile.SourceWidth.ToString(
                    CultureInfo.InvariantCulture),
            }));
    }

    public void Publish(RecordingSessionStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        Enqueue(new DiagnosticLogEntry(
            TimestampUtc(),
            DiagnosticLogLevel.Information,
            "recording.media_statistics",
            new Dictionary<string, string>
            {
                ["audioVideoOffsetMicroseconds"] = Microseconds(
                    statistics.AudioVideoOffset),
                ["droppedSourceVideoFrameCount"] =
                    statistics.DroppedSourceVideoFrameCount.ToString(
                        CultureInfo.InvariantCulture),
                ["duplicatedOutputVideoFrameCount"] =
                    statistics.DuplicatedOutputVideoFrameCount.ToString(
                        CultureInfo.InvariantCulture),
                ["latestEncodeLatencyMicroseconds"] = Microseconds(
                    statistics.LatestEncodeLatency),
                ["maximumEncodeLatencyMicroseconds"] = Microseconds(
                    statistics.MaximumEncodeLatency),
                ["muxedAudioPacketCount"] = statistics.MuxedAudioPacketCount
                    .ToString(CultureInfo.InvariantCulture),
                ["muxedVideoPacketCount"] = statistics.MuxedVideoPacketCount
                    .ToString(CultureInfo.InvariantCulture),
                ["sourceVideoFrameCount"] = statistics.SourceVideoFrameCount
                    .ToString(CultureInfo.InvariantCulture),
            }));
    }

    public void Publish(RecordingAvDriftEvent drift)
    {
        ArgumentNullException.ThrowIfNull(drift);
        Enqueue(new DiagnosticLogEntry(
            TimestampUtc(),
            DiagnosticLogLevel.Warning,
            "recording.av_drift_exceeded",
            new Dictionary<string, string>
            {
                ["absoluteDriftMicroseconds"] = Microseconds(
                    drift.AbsoluteDrift),
                ["audioPtsMicroseconds"] = Microseconds(drift.AudioPts),
                ["videoPtsMicroseconds"] = Microseconds(drift.VideoPts),
            }));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _queuedEntries.Writer.TryComplete();
        }

        if (Volatile.Read(ref _deliveryThreadId) !=
            Environment.CurrentManagedThreadId)
        {
            _queuedWorker.GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }

    private void Enqueue(DiagnosticLogEntry entry)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _queuedEntries.Writer.TryWrite(entry);
        }
    }

    private async Task WriteQueuedEntriesAsync()
    {
        await foreach (var entry in _queuedEntries.Reader.ReadAllAsync())
        {
            try
            {
                Volatile.Write(
                    ref _deliveryThreadId,
                    Environment.CurrentManagedThreadId);
                await WriteBestEffortAsync(entry, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _deliveryThreadId, 0);
            }
        }
    }

    private async Task WriteBestEffortAsync(
        DiagnosticLogEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _log.WriteAsync(entry, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ObjectDisposedException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Structured diagnostic logging failed: {0}",
                exception.GetType().Name);
        }
    }

    private DateTimeOffset TimestampUtc() =>
        _clock.LocalNow.ToUniversalTime();

    private static string StorageStateName(
        Domain.Storage.RecordingStorageState state) => state switch
        {
            Domain.Storage.RecordingStorageState.Healthy => "healthy",
            Domain.Storage.RecordingStorageState.Warning => "warning",
            Domain.Storage.RecordingStorageState.StopRequired =>
                "stop_required",
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "The recording storage state is not supported."),
        };

    private static string CameraWarningReasonName(
        CameraRestoreWarningReason reason) => reason switch
        {
            CameraRestoreWarningReason.RecordingCompleted =>
                "recording_completed",
            CameraRestoreWarningReason.StartCanceled => "start_canceled",
            CameraRestoreWarningReason.NoSignal => "no_signal",
            CameraRestoreWarningReason.InsufficientStorage =>
                "insufficient_storage",
            CameraRestoreWarningReason.StartFailed => "start_failed",
            CameraRestoreWarningReason.StaleLeaseRecovery =>
                "stale_lease_recovery",
            _ => throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "The camera warning reason is not supported."),
        };

    private static string AudioInputName(AudioInput input) => input switch
    {
        AudioInput.Desktop => "desktop",
        AudioInput.Microphone => "microphone",
        _ => throw new ArgumentOutOfRangeException(
            nameof(input),
            input,
            "The audio input is not supported."),
    };

    private static string AudioWarningKindName(
        AudioSessionWarningKind kind) => kind switch
        {
            AudioSessionWarningKind.InputUnavailable => "input_unavailable",
            AudioSessionWarningKind.EndpointRediscoveryFailed =>
                "endpoint_rediscovery_failed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The audio warning kind is not supported."),
        };

    private static string AudioStatusKindName(
        AudioSessionStatusKind kind) => kind switch
        {
            AudioSessionStatusKind.EndpointRediscoveryScheduled =>
                "endpoint_rediscovery_scheduled",
            AudioSessionStatusKind.InputRecovered => "input_recovered",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The audio status kind is not supported."),
        };

    private static string EncoderName(EncoderKind encoder) => encoder switch
    {
        EncoderKind.Nvenc => "nvenc",
        EncoderKind.Amf => "amf",
        EncoderKind.Qsv => "qsv",
        EncoderKind.MediaFoundationSoftware => "media_foundation_software",
        _ => throw new ArgumentOutOfRangeException(
            nameof(encoder),
            encoder,
            "The encoder kind is not supported."),
    };

    private static string GpuVendorName(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Unknown => "unknown",
        GpuVendor.Nvidia => "nvidia",
        GpuVendor.Amd => "amd",
        GpuVendor.Intel => "intel",
        _ => throw new ArgumentOutOfRangeException(
            nameof(vendor),
            vendor,
            "The GPU vendor is not supported."),
    };

    private static string PixelFormatName(VideoPixelFormat format) =>
        format switch
        {
            VideoPixelFormat.Bgra8 => "bgra8",
            VideoPixelFormat.Rgba8 => "rgba8",
            VideoPixelFormat.Nv12 => "nv12",
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The source pixel format is not supported."),
        };

    private static string Microseconds(TimeSpan value) =>
        value.TotalMicroseconds.ToString(
            "0.###",
            CultureInfo.InvariantCulture);
}
