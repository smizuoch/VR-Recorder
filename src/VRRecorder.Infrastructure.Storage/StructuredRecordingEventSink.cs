using System.Globalization;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Infrastructure.Storage;

public sealed class StructuredRecordingEventSink
    : IRecordingStorageStatusSink,
      ISavedRecordingSink,
      ICameraRestoreWarningSink,
      IAudioSessionEventSink
{
    private readonly RotatingJsonLinesDiagnosticLog _log;
    private readonly IWallClock _clock;

    public StructuredRecordingEventSink(
        RotatingJsonLinesDiagnosticLog log,
        IWallClock clock)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);
        _log = log;
        _clock = clock;
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

        WriteBestEffort(new DiagnosticLogEntry(
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

        WriteBestEffort(new DiagnosticLogEntry(
            TimestampUtc(),
            DiagnosticLogLevel.Information,
            "audio.input_status",
            fields));
    }

    private void WriteBestEffort(DiagnosticLogEntry entry) =>
        WriteBestEffortAsync(entry, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

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
}
