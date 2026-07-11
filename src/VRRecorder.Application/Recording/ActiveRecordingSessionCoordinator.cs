using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Recording;

public sealed class ActiveRecordingSessionCoordinator
    : IRecordingSessionActivator,
      IStopRequestSink,
      IActiveRecordingAudioCommands
{
    private readonly object _gate = new();
    private readonly IRecordingEngine _recordingEngine;
    private readonly RecordingFileFinalizationUseCase _finalization;
    private readonly IRecordingAudioRoutingGateway _audioRoutingGateway;
    private ActiveSession? _activeSession;
    private RecorderState _state = RecorderState.Ready;
    private RecordingStopReason? _stopReason;

    public ActiveRecordingSessionCoordinator(
        IRecordingEngine recordingEngine,
        RecordingFileFinalizationUseCase finalization)
        : this(
            recordingEngine,
            finalization,
            recordingEngine as IRecordingAudioRoutingGateway ??
            UnsupportedAudioRoutingGateway.Instance)
    {
    }

    public ActiveRecordingSessionCoordinator(
        IRecordingEngine recordingEngine,
        RecordingFileFinalizationUseCase finalization,
        IRecordingAudioRoutingGateway audioRoutingGateway)
    {
        ArgumentNullException.ThrowIfNull(recordingEngine);
        ArgumentNullException.ThrowIfNull(finalization);
        ArgumentNullException.ThrowIfNull(audioRoutingGateway);
        _recordingEngine = recordingEngine;
        _finalization = finalization;
        _audioRoutingGateway = audioRoutingGateway;
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

    public RecordingAudioControlState? CurrentAudioControlState
    {
        get
        {
            lock (_gate)
            {
                return _activeSession?.AudioControlState;
            }
        }
    }

    public void Activate(
        RecordingHandle handle,
        CancellationToken sessionLifetimeToken = default,
        IRecordingSessionCompletionSink? completionSink = null) =>
        Activate(
            handle,
            AudioRouting.Mixed,
            sessionLifetimeToken,
            completionSink);

    public void Activate(
        RecordingHandle handle,
        AudioRouting initialAudioRouting,
        CancellationToken sessionLifetimeToken = default,
        IRecordingSessionCompletionSink? completionSink = null)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var audioControlState = RecordingAudioControlState.FromRouting(
            initialAudioRouting);
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
                completionSink,
                audioControlState);
            _stopReason = null;
            _state = RecorderStateMachine.Transition(
                RecorderState.Starting,
                RecorderTrigger.FirstPacketCommitted);
        }
    }

    public Task<RecordingAudioControlState> ExecuteAudioCommandAsync(
        RecordingAudioCommand command,
        CancellationToken cancellationToken)
    {
        ActiveSession session;
        Task predecessor;
        var completion = new TaskCompletionSource<RecordingAudioControlState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            session = _activeSession ?? throw new InvalidOperationException(
                "No recording session is active.");
            if (session.StopTask is not null)
            {
                throw new InvalidOperationException(
                    "The active recording session is stopping.");
            }

            predecessor = session.AudioCommandTail;
            session.AudioCommandTail = ProcessAudioCommandAsync(
                session,
                predecessor,
                command,
                completion,
                cancellationToken);
        }

        return completion.Task;
    }

    private async Task ProcessAudioCommandAsync(
        ActiveSession session,
        Task predecessor,
        RecordingAudioCommand command,
        TaskCompletionSource<RecordingAudioControlState> completion,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        await predecessor.ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordingAudioControlState next;
            lock (_gate)
            {
                if (!ReferenceEquals(_activeSession, session))
                {
                    throw new InvalidOperationException(
                        "The recording session changed before the audio update.");
                }

                next = session.AudioControlState.Apply(command);
            }

            await _audioRoutingGateway
                .UpdateAudioRoutingAsync(
                    session.Handle,
                    next.EffectiveRouting,
                    cancellationToken)
                .ConfigureAwait(false);

            lock (_gate)
            {
                if (!ReferenceEquals(_activeSession, session))
                {
                    throw new InvalidOperationException(
                        "The recording session changed during the audio update.");
                }

                session.AudioControlState = next;
            }

            completion.TrySetResult(next);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
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
            await session.AudioCommandTail.ConfigureAwait(false);
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
        IRecordingSessionCompletionSink? completionSink,
        RecordingAudioControlState audioControlState)
    {
        public RecordingHandle Handle { get; } = handle;

        public RecordingSessionStopCoordinator Coordinator { get; } = coordinator;

        public IRecordingSessionCompletionSink? CompletionSink { get; } =
            completionSink;

        public RecordingStopReason? StopReason { get; set; }

        public Task? StopTask { get; set; }

        public RecordingAudioControlState AudioControlState { get; set; } =
            audioControlState;

        public Task AudioCommandTail { get; set; } = Task.CompletedTask;
    }

    private sealed class UnsupportedAudioRoutingGateway
        : IRecordingAudioRoutingGateway
    {
        public static UnsupportedAudioRoutingGateway Instance { get; } = new();

        public Task UpdateAudioRoutingAsync(
            RecordingHandle handle,
            AudioRouting routing,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "The recording engine does not support runtime audio routing updates.");
    }
}
