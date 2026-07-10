using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingRuntime : IDesktopRecordingRuntime
{
    private readonly object _gate = new();
    private readonly IDesktopRecordingStartRequestSource _requests;
    private readonly IRecordingLifecycleController _lifecycle;
    private readonly IStopRequestSink _stopRequests;
    private readonly CancellationTokenSource _lifetime = new();
    private RecordingHandle? _activeHandle;
    private Task? _sessionStopTask;
    private Task? _toggleTask;
    private Task? _disposeTask;
    private bool _disposed;

    public DesktopRecordingRuntime(
        IDesktopRecordingStartRequestSource requests,
        IRecordingLifecycleController lifecycle,
        IStopRequestSink stopRequests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(stopRequests);
        _requests = requests;
        _lifecycle = lifecycle;
        _stopRequests = stopRequests;
    }

    public Task ToggleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_toggleTask is null || _toggleTask.IsCompleted)
            {
                _toggleTask = RunToggleAsync(cancellationToken);
            }

            operation = _toggleTask;
        }

        return cancellationToken.CanBeCanceled
            ? operation.WaitAsync(cancellationToken)
            : operation;
    }

    public ValueTask DisposeAsync()
    {
        bool cancelLifetime = false;
        Task disposal;
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                cancelLifetime = true;
                _disposeTask = DisposeCoreAsync(_toggleTask);
            }

            disposal = _disposeTask;
        }

        if (cancelLifetime)
        {
            CancelLifetime();
        }

        return new ValueTask(disposal);
    }

    private async Task RunToggleAsync(CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        await ToggleCoreAsync(operation.Token).ConfigureAwait(false);
    }

    private async Task ToggleCoreAsync(CancellationToken cancellationToken)
    {
        switch (_lifecycle.State)
        {
            case RecorderState.Ready:
            case RecorderState.NoSignal:
                ClearCompletedSession();
                await StartAsync(cancellationToken).ConfigureAwait(false);
                return;
            case RecorderState.Recording:
            case RecorderState.SignalLost:
            case RecorderState.Stopping:
                await StopActiveSessionAsync(
                        RecordingStopReason.UserRequested,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            default:
                throw new InvalidOperationException(
                    $"Desktop recording cannot toggle while the lifecycle is {_lifecycle.State}.");
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var request = await _requests
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(request);
        var result = await _lifecycle
            .StartAsync(
                request.SelectedServiceId,
                request.Command,
                cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(result);
        CaptureStartResult(result);
    }

    private void CaptureStartResult(RecordingLifecycleStartResult result)
    {
        var lifecycleState = _lifecycle.State;
        if (result.Recording is StartRecordingResult.Started started)
        {
            if (HasActiveOrStoppingSession(lifecycleState))
            {
                _activeHandle = started.Handle;
                return;
            }

            if (lifecycleState is RecorderState.Ready or RecorderState.Faulted)
            {
                ClearCompletedSession();
                return;
            }

            throw InconsistentStartResult(result, lifecycleState);
        }

        if (HasActiveOrStoppingSession(lifecycleState))
        {
            throw InconsistentStartResult(result, lifecycleState);
        }

        ClearCompletedSession();
    }

    private async Task StopActiveSessionAsync(
        RecordingStopReason reason,
        CancellationToken cancellationToken)
    {
        var handle = _activeHandle ?? throw new InvalidOperationException(
            "The recording lifecycle has no desktop-owned active handle.");
        _sessionStopTask ??= _stopRequests.RequestStopAsync(
            new RecordingStopRequest(handle, reason),
            CancellationToken.None);
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                await _sessionStopTask
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _sessionStopTask.ConfigureAwait(false);
            }
        }
        finally
        {
            if (_sessionStopTask.IsCompleted &&
                !HasActiveOrStoppingSession(_lifecycle.State))
            {
                ClearCompletedSession();
            }
        }
    }

    private async Task DisposeCoreAsync(Task? operation)
    {
        await Task.Yield();
        await ObserveWithoutInterruptingShutdownAsync(operation)
            .ConfigureAwait(false);
        try
        {
            if (HasActiveOrStoppingSession(_lifecycle.State))
            {
                await StopActiveSessionAsync(
                        RecordingStopReason.ApplicationShutdown,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                ClearCompletedSession();
            }
        }
        finally
        {
            try
            {
                _lifecycle.Dispose();
            }
            finally
            {
                _lifetime.Dispose();
            }
        }
    }

    private void ClearCompletedSession()
    {
        _activeHandle = null;
        _sessionStopTask = null;
    }

    private void CancelLifetime()
    {
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent DisposeAsync already completed the same shutdown.
        }
    }

    private static bool HasActiveOrStoppingSession(RecorderState state) =>
        state is RecorderState.Recording or
            RecorderState.SignalLost or
            RecorderState.Stopping;

    private static InvalidOperationException InconsistentStartResult(
        RecordingLifecycleStartResult result,
        RecorderState lifecycleState) =>
        new(
            $"The recording start result {result.Recording?.GetType().Name ?? "None"} " +
            $"is inconsistent with lifecycle state {lifecycleState}.");

    private static async Task ObserveWithoutInterruptingShutdownAsync(
        Task? operation)
    {
        if (operation is null)
        {
            return;
        }

        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Shutdown must still safely stop any session activated by the operation.
        }
    }
}
