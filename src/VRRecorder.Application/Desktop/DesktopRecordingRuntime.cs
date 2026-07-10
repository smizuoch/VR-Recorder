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
    private Task? _transportToggleTask;
    private CancellationTokenSource? _startCancellation;
    private bool _semanticStartCancellationRequested;
    private Task? _shutdownTask;
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
        CancellationTokenSource? cancelStart = null;
        Task operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_transportToggleTask is null || _transportToggleTask.IsCompleted)
            {
                var lifecycleState = _lifecycle.State;
                var isStart = lifecycleState is
                    RecorderState.Ready or RecorderState.NoSignal;
                var operationCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _lifetime.Token);
                if (isStart)
                {
                    _startCancellation = operationCancellation;
                    _semanticStartCancellationRequested = false;
                }

                _transportToggleTask = RunTransportToggleAsync(
                    operationCancellation,
                    isStart,
                    cancellationToken);
            }
            else if (IsCancelableStartPhase(_lifecycle.State) &&
                     _startCancellation is not null)
            {
                _semanticStartCancellationRequested = true;
                cancelStart = _startCancellation;
            }

            operation = _transportToggleTask;
        }

        cancelStart?.Cancel();
        return WaitForCallerAsync(operation, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(ShutdownAsync(
            RecordingStopReason.ApplicationShutdown));
    }

    public Task ShutdownAsync(RecordingStopReason reason)
    {
        bool cancelLifetime = false;
        Task shutdown;
        if (reason is not RecordingStopReason.ApplicationShutdown and
            not RecordingStopReason.ComplianceFault)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "A desktop runtime shutdown requires a terminal application reason.");
        }

        lock (_gate)
        {
            if (_shutdownTask is null)
            {
                _disposed = true;
                cancelLifetime = true;
                _shutdownTask = ShutdownCoreAsync(
                    _transportToggleTask,
                    reason);
            }

            shutdown = _shutdownTask;
        }

        if (cancelLifetime)
        {
            CancelLifetime();
        }

        return shutdown;
    }

    private async Task RunTransportToggleAsync(
        CancellationTokenSource operationCancellation,
        bool isStart,
        CancellationToken initiatingCallerToken)
    {
        await Task.Yield();
        try
        {
            await ToggleCoreAsync(operationCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            isStart &&
            IsSemanticStartCancellation(
                operationCancellation,
                initiatingCallerToken))
        {
            // A second REC activation during Arming or Countdown is a normal
            // convergence to Ready, not an exceptional caller cancellation.
        }
        finally
        {
            if (isStart)
            {
                ClearStartCancellation(operationCancellation);
            }

            operationCancellation.Dispose();
        }
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
        await CaptureStartResultAsync(result).ConfigureAwait(false);
    }

    private async Task CaptureStartResultAsync(
        RecordingLifecycleStartResult result)
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

            var inconsistency = InconsistentStartResult(result, lifecycleState);
            _activeHandle = started.Handle;
            await StopActiveSessionAsync(
                    RecordingStopReason.InvariantViolation,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw inconsistency;
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
            await WaitForCallerAsync(_sessionStopTask, cancellationToken)
                .ConfigureAwait(false);
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

    private async Task ShutdownCoreAsync(
        Task? operation,
        RecordingStopReason reason)
    {
        await Task.Yield();
        await ObserveWithoutInterruptingShutdownAsync(operation)
            .ConfigureAwait(false);
        try
        {
            if (HasActiveOrStoppingSession(_lifecycle.State))
            {
                await StopActiveSessionAsync(
                        reason,
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

    private static bool IsCancelableStartPhase(RecorderState state) =>
        state is RecorderState.Arming or RecorderState.Countdown;

    private bool IsSemanticStartCancellation(
        CancellationTokenSource operationCancellation,
        CancellationToken initiatingCallerToken)
    {
        lock (_gate)
        {
            return ReferenceEquals(
                       _startCancellation,
                       operationCancellation) &&
                   _semanticStartCancellationRequested &&
                   !initiatingCallerToken.IsCancellationRequested &&
                   !_lifetime.IsCancellationRequested;
        }
    }

    private void ClearStartCancellation(
        CancellationTokenSource operationCancellation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(
                    _startCancellation,
                    operationCancellation))
            {
                return;
            }

            _startCancellation = null;
            _semanticStartCancellationRequested = false;
        }
    }

    private static InvalidOperationException InconsistentStartResult(
        RecordingLifecycleStartResult result,
        RecorderState lifecycleState) =>
        new(
            $"The recording start result {result.Recording?.GetType().Name ?? "None"} " +
            $"is inconsistent with lifecycle state {lifecycleState}.");

    private static Task WaitForCallerAsync(
        Task operation,
        CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled
            ? operation.WaitAsync(cancellationToken)
            : operation;

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
