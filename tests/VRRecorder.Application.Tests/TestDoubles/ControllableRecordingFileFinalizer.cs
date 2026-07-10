using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class ControllableRecordingFileFinalizer : IRecordingFileFinalizer
{
    private readonly TaskCompletionSource _requested = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<FinalizedRecording> _completed = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<FinalizedRecording> FinalizeAsync(
        PendingRecording recording,
        CancellationToken cancellationToken)
    {
        _requested.TrySetResult();
        return _completed.Task.WaitAsync(cancellationToken);
    }

    public Task WaitUntilRequestedAsync() => _requested.Task;

    public void Complete(FinalizedRecording recording) =>
        _completed.TrySetResult(recording);
}
