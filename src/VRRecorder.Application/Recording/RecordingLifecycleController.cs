using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed class RecordingLifecycleController : IDisposable
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly VrChatCameraConnectionUseCase _cameraConnections;
    private readonly ICameraLeaseStore _cameraLeases;
    private readonly StartRecordingUseCase _startRecording;
    private readonly IStopRequestSink _stopRequests;
    private VideoSignalSupervisor? _videoSignal;
    private RecorderState _state = RecorderState.Ready;

    public RecordingLifecycleController(
        VrChatCameraConnectionUseCase cameraConnections,
        ICameraLeaseStore cameraLeases,
        StartRecordingUseCase startRecording,
        IStopRequestSink stopRequests)
    {
        ArgumentNullException.ThrowIfNull(cameraConnections);
        ArgumentNullException.ThrowIfNull(cameraLeases);
        ArgumentNullException.ThrowIfNull(startRecording);
        ArgumentNullException.ThrowIfNull(stopRequests);
        _cameraConnections = cameraConnections;
        _cameraLeases = cameraLeases;
        _startRecording = startRecording;
        _stopRequests = stopRequests;
    }

    public RecorderState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public async Task<RecordingLifecycleStartResult> StartAsync(
        string? selectedServiceId,
        CameraSnapshot cameraSnapshot,
        StartRecordingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cameraSnapshot);
        ArgumentNullException.ThrowIfNull(command);
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (State != RecorderState.Ready)
            {
                throw new InvalidOperationException(
                    $"Recording cannot start while the lifecycle is {State}.");
            }

            var connection = await _cameraConnections
                .ResolveAsync(selectedServiceId, cancellationToken)
                .ConfigureAwait(false);
            if (connection is not VrChatCameraConnectionResolution.Connected connected)
            {
                return new RecordingLifecycleStartResult(
                    State,
                    connection,
                    Recording: null);
            }

            SetState(RecorderStateMachine.Transition(
                RecorderState.Ready,
                RecorderTrigger.StartRequested));
            var camera = new CameraSessionController(
                connected.Gateway,
                _cameraLeases);
            await camera
                .AcquireAsync(cameraSnapshot, cancellationToken)
                .ConfigureAwait(false);
            var recording = await _startRecording
                .ExecuteAsync(command, cancellationToken)
                .ConfigureAwait(false);
            var state = recording switch
            {
                StartRecordingResult.Started => RecorderStateMachine.Transition(
                    RecorderState.Starting,
                    RecorderTrigger.FirstPacketCommitted),
                StartRecordingResult.NoSignal => RecorderStateMachine.Transition(
                    RecorderState.Arming,
                    RecorderTrigger.SignalTimeout),
                StartRecordingResult.InsufficientStorage => RecorderState.Ready,
                _ => throw new InvalidOperationException(
                    $"Unknown recording start result {recording.GetType().Name}."),
            };
            if (recording is StartRecordingResult.Started started)
            {
                _videoSignal = new VideoSignalSupervisor(
                    started.Handle,
                    _stopRequests);
            }

            SetState(state);
            return new RecordingLifecycleStartResult(
                state,
                connection,
                recording);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ObserveFreshVideoFrameAsync(
        VideoFrameObservation frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var supervisor = ActiveVideoSignalSupervisor();
            supervisor.ObserveFreshFrame(frame);
            if (State == RecorderState.SignalLost)
            {
                SetState(RecorderStateMachine.Transition(
                    RecorderState.SignalLost,
                    RecorderTrigger.SignalRecovered));
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<VideoSignalStatus> EvaluateVideoSignalAsync(
        MonotonicTimestamp now,
        CancellationToken cancellationToken)
    {
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var status = await ActiveVideoSignalSupervisor()
                .EvaluateAsync(now, cancellationToken)
                .ConfigureAwait(false);
            if (status == VideoSignalStatus.SignalLost &&
                State == RecorderState.Recording)
            {
                SetState(RecorderStateMachine.Transition(
                    RecorderState.Recording,
                    RecorderTrigger.FreshFrameTimeout));
            }
            else if (status == VideoSignalStatus.SafeStop &&
                     State == RecorderState.SignalLost)
            {
                var stopping = RecorderStateMachine.Transition(
                    RecorderState.SignalLost,
                    RecorderTrigger.GraceExpired);
                SetState(RecorderStateMachine.Transition(
                    stopping,
                    RecorderTrigger.StopCompleted));
                _videoSignal = null;
            }

            return status;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose() => _operationGate.Dispose();

    private void SetState(RecorderState state)
    {
        lock (_stateGate)
        {
            _state = state;
        }
    }

    private VideoSignalSupervisor ActiveVideoSignalSupervisor() =>
        _videoSignal ?? throw new InvalidOperationException(
            "Video signal monitoring requires an active recording.");
}
