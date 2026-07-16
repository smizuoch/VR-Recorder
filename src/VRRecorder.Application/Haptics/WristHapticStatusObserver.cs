using System.Threading.Channels;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Haptics;

public sealed class WristHapticStatusObserver : IAsyncDisposable
{
    private readonly Channel<RecorderStatusSnapshot> _statuses =
        Channel.CreateUnbounded<RecorderStatusSnapshot>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
    private readonly WristHapticFeedbackCoordinator _feedback;
    private readonly Action<Exception>? _failureObserver;
    private readonly IDisposable _subscription;
    private readonly Task _pump;
    private int _disposeStarted;

    public WristHapticStatusObserver(
        IRecorderStatusSource statuses,
        WristHapticFeedbackCoordinator feedback,
        Action<Exception>? failureObserver = null)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(feedback);
        _feedback = feedback;
        _failureObserver = failureObserver;
        _pump = RunAsync();
        try
        {
            _subscription = statuses.Subscribe(Observe);
        }
        catch
        {
            _statuses.Writer.TryComplete();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
        {
            _subscription.Dispose();
            _statuses.Writer.TryComplete();
        }

        return new ValueTask(_pump);
    }

    private void Observe(RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _statuses.Writer.TryWrite(status);
    }

    private async Task RunAsync()
    {
        var initialized = false;
        var previous = RecorderState.Booting;
        var recordingActive = false;
        await foreach (var status in _statuses.Reader.ReadAllAsync())
        {
            if (!initialized)
            {
                initialized = true;
                previous = status.State;
                recordingActive = IsRecordingLifecycle(status.State);
                continue;
            }

            if (status.State == previous)
            {
                continue;
            }

            previous = status.State;
            var kind = SelectFeedback(status.State, ref recordingActive);
            if (kind is null)
            {
                continue;
            }

            var result = await _feedback
                .PublishAsync(
                    status.Revision,
                    kind.Value,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (result is WristHapticFeedbackResult.Failed failed)
            {
                ReportFailureBestEffort(failed.Failure);
            }
        }
    }

    private static WristHapticFeedbackKind? SelectFeedback(
        RecorderState state,
        ref bool recordingActive)
    {
        switch (state)
        {
            case RecorderState.Recording when !recordingActive:
                recordingActive = true;
                return WristHapticFeedbackKind.RecordingStarted;
            case RecorderState.Ready when recordingActive:
                recordingActive = false;
                return WristHapticFeedbackKind.RecordingStopped;
            case RecorderState.SignalLost:
                return WristHapticFeedbackKind.Fault;
            case RecorderState.NoSignal:
                recordingActive = false;
                return WristHapticFeedbackKind.Fault;
            case RecorderState.Faulted:
            case RecorderState.ComplianceFault:
                recordingActive = false;
                return WristHapticFeedbackKind.Fault;
            default:
                return null;
        }
    }

    private static bool IsRecordingLifecycle(RecorderState state) =>
        state is RecorderState.Recording or
            RecorderState.SignalLost or
            RecorderState.Stopping;

    private void ReportFailureBestEffort(Exception failure)
    {
        try
        {
            _failureObserver?.Invoke(failure);
        }
        catch (Exception)
        {
            // Diagnostics must not alter recorder status delivery.
        }
    }
}
