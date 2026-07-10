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
            var connection = await _cameraConnections
                .ResolveAsync(selectedServiceId, cancellationToken)
                .ConfigureAwait(false);
            if (connection is not VrChatCameraConnectionResolution.Connected)
            {
                return new RecordingLifecycleStartResult(
                    State,
                    connection,
                    Recording: null);
            }

            throw new NotSupportedException(
                "Starting a selected VRChat recording is not implemented.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose() => _operationGate.Dispose();
}
