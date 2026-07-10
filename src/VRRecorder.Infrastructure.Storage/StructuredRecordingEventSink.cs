using System.Globalization;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class StructuredRecordingEventSink
    : IRecordingStorageStatusSink,
      ISavedRecordingSink,
      ICameraRestoreWarningSink
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
}
