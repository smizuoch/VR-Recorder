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
        CancellationToken sessionLifetimeToken = default,
        IRecordingSessionCompletionSink? completionSink = null)
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
                    sessionLifetimeToken),
                completionSink);
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
                session.StopReason = request.Reason;
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
        RecordingFinalizationResult? result = null;
        Exception? primaryFailure = null;
        try
        {
            result = await session.Coordinator
                .StopAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        var finalState = primaryFailure is null &&
                         result is RecordingFinalizationResult.Saved
            ? RecorderStateMachine.Transition(
                RecorderState.Stopping,
                RecorderTrigger.StopCompleted)
            : RecorderState.Faulted;
        Exception? completionFailure = null;
        try
        {
            if (session.CompletionSink is not null)
            {
                await session.CompletionSink
                    .CompleteAsync(
                        new RecordingSessionCompletion(
                            session.Handle,
                            session.StopReason ?? throw new InvalidOperationException(
                                "A terminal recording session has no stop reason."),
                            finalState),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _state = finalState;
                _activeSession = null;
            }
        }

        if (primaryFailure is not null)
        {
            completion.TrySetException(primaryFailure);
        }
        else if (completionFailure is not null)
        {
            completion.TrySetException(completionFailure);
        }
        else
        {
            completion.TrySetResult();
        }
    }

    private sealed class ActiveSession(
        RecordingHandle handle,
        RecordingSessionStopCoordinator coordinator,
        IRecordingSessionCompletionSink? completionSink)
    {
        public RecordingHandle Handle { get; } = handle;

        public RecordingSessionStopCoordinator Coordinator { get; } = coordinator;

        public IRecordingSessionCompletionSink? CompletionSink { get; } =
            completionSink;

        public RecordingStopReason? StopReason { get; set; }

        public Task? StopTask { get; set; }
    }
}
