using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingEngine : IRecordingEngine
{
    private readonly TaskCompletionSource<RecordingResult> _stopCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public List<string> CreatedFiles { get; } = [];

    public Task<RecordingHandle> StartAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        StartCallCount++;
        CreatedFiles.Add("recording.recording.mp4");
        return Task.FromResult(new RecordingHandle("test-recording"));
    }

    public Task<RecordingResult> StopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken)
    {
        StopCallCount++;
        return _stopCompletion.Task.WaitAsync(cancellationToken);
    }

    public void CompleteStop(RecordingResult result) =>
        _stopCompletion.TrySetResult(result);
}
