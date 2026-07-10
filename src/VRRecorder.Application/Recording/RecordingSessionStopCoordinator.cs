using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Recording;

public sealed class RecordingSessionStopCoordinator
{
    private readonly object _gate = new();
    private readonly IRecordingEngine _recordingEngine;
    private readonly RecordingHandle _handle;
    private readonly RecordingFileFinalizationUseCase _finalization;
    private readonly CancellationToken _sessionLifetimeToken;
    private Task<RecordingFinalizationResult>? _stopTask;

    public RecordingSessionStopCoordinator(
        IRecordingEngine recordingEngine,
        RecordingHandle handle,
        RecordingFileFinalizationUseCase finalization,
        CancellationToken sessionLifetimeToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordingEngine);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(finalization);
        _recordingEngine = recordingEngine;
        _handle = handle;
        _finalization = finalization;
        _sessionLifetimeToken = sessionLifetimeToken;
    }

    public Task<RecordingFinalizationResult> StopAsync()
    {
        lock (_gate)
        {
            return _stopTask ??= StopAndFinalizeAsync();
        }
    }

    private async Task<RecordingFinalizationResult> StopAndFinalizeAsync()
    {
        var stopped = await _recordingEngine
            .StopAsync(_handle, _sessionLifetimeToken)
            .ConfigureAwait(false);
        return await _finalization
            .ExecuteAsync(stopped.Recording, _sessionLifetimeToken)
            .ConfigureAwait(false);
    }
}
