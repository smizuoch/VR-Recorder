using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Recording;

public sealed class RecordingStopCoordinator
{
    private readonly object _gate = new();
    private readonly IRecordingEngine _recordingEngine;
    private readonly RecordingHandle _handle;
    private readonly CancellationToken _sessionLifetimeToken;
    private Task<RecordingStopResult>? _stopTask;

    public RecordingStopCoordinator(
        IRecordingEngine recordingEngine,
        RecordingHandle handle,
        CancellationToken sessionLifetimeToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordingEngine);
        ArgumentNullException.ThrowIfNull(handle);

        _recordingEngine = recordingEngine;
        _handle = handle;
        _sessionLifetimeToken = sessionLifetimeToken;
    }

    public Task<RecordingStopResult> StopAsync()
    {
        lock (_gate)
        {
            return _stopTask ??= _recordingEngine.StopAsync(
                _handle,
                _sessionLifetimeToken);
        }
    }
}
