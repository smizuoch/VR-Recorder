using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Recording;

public sealed class ActiveRecordingSessionCoordinator
    : IRecordingSessionActivator, IStopRequestSink
{
    private readonly object _gate = new();
    private readonly IRecordingEngine _recordingEngine;
    private readonly RecordingFileFinalizationUseCase _finalization;
    private ActiveSession? _activeSession;
    private RecorderState _state = RecorderState.Ready;
    private RecordingStopReason? _stopReason;

    public ActiveRecordingSessionCoordinator(
        IRecordingEngine recordingEngine,
        RecordingFileFinalizationUseCase finalization)
    {
        ArgumentNullException.ThrowIfNull(recordingEngine);
        ArgumentNullException.ThrowIfNull(finalization);
        _recordingEngine = recordingEngine;
        _finalization = finalization;
    }

    public RecorderState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public RecordingStopReason? StopReason
    {
        get
        {
            lock (_gate)
            {
                return _stopReason;
            }
        }
    }

    public void Activate(
        RecordingHandle handle,
        CancellationToken sessionLifetimeToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        lock (_gate)
        {
            if (_activeSession is not null)
            {
                throw new InvalidOperationException(
                    "A recording session is already active.");
            }

            _activeSession = new ActiveSession(
                handle,
                new RecordingSessionStopCoordinator(
                    _recordingEngine,
                    handle,
                    _finalization,
                    sessionLifetimeToken));
            _stopReason = null;
            _state = RecorderStateMachine.Transition(
                RecorderState.Starting,
                RecorderTrigger.FirstPacketCommitted);
        }
    }

    public Task RequestStopAsync(
        RecordingStopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Task stopTask;
        lock (_gate)
        {
            if (_activeSession is null ||
                _activeSession.Handle != request.Handle)
            {
                return Task.CompletedTask;
            }

            var session = _activeSession;
            if (session.StopTask is null)
            {
                _stopReason = request.Reason;
                _state = RecorderStateMachine.Transition(
                    _state,
                    RecorderTrigger.StopRequested);
                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.StopTask = completion.Task;
                _ = CompleteStopAsync(session, completion);
            }

            stopTask = session.StopTask;
        }

        return cancellationToken.CanBeCanceled
            ? stopTask.WaitAsync(cancellationToken)
            : stopTask;
    }

    private async Task CompleteStopAsync(
        ActiveSession session,
        TaskCompletionSource completion)
    {
        try
        {
            var result = await session.Coordinator
                .StopAsync()
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (!ReferenceEquals(_activeSession, session))
                {
                    completion.TrySetResult();
                    return;
                }

                _state = result is RecordingFinalizationResult.Saved
                    ? RecorderStateMachine.Transition(
                        _state,
                        RecorderTrigger.StopCompleted)
                    : RecorderState.Faulted;
                _activeSession = null;
            }

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _state = RecorderState.Faulted;
                    _activeSession = null;
                }
            }

            completion.TrySetException(exception);
        }
    }

    private sealed class ActiveSession(
        RecordingHandle handle,
        RecordingSessionStopCoordinator coordinator)
    {
        public RecordingHandle Handle { get; } = handle;

        public RecordingSessionStopCoordinator Coordinator { get; } = coordinator;

        public Task? StopTask { get; set; }
    }
}
