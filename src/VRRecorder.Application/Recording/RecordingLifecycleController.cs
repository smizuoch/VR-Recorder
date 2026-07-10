using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Recording;

public sealed class RecordingLifecycleController : IDisposable
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly VrChatCameraConnectionUseCase _cameraConnections;
    private readonly ICameraLeaseStore _cameraLeases;
    private readonly StartRecordingUseCase _startRecording;
    private RecorderState _state = RecorderState.Ready;

    public RecordingLifecycleController(
        VrChatCameraConnectionUseCase cameraConnections,
        ICameraLeaseStore cameraLeases,
        StartRecordingUseCase startRecording)
    {
        ArgumentNullException.ThrowIfNull(cameraConnections);
        ArgumentNullException.ThrowIfNull(cameraLeases);
        ArgumentNullException.ThrowIfNull(startRecording);
        _cameraConnections = cameraConnections;
        _cameraLeases = cameraLeases;
        _startRecording = startRecording;
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

    public void Dispose() => _operationGate.Dispose();

    private void SetState(RecorderState state)
    {
        lock (_stateGate)
        {
            _state = state;
        }
    }
}
