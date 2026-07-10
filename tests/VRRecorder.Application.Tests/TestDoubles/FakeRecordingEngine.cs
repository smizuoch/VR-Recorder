using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingEngine : IRecordingEngine
{
    private readonly TaskCompletionSource _startRequested = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<RecordingHandle> _firstPacketCommitted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<RecordingStopResult> _stopCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public List<string> CreatedFiles { get; } = [];

    public List<RecordingPlan> StartedPlans { get; } = [];

    public List<CancellationToken> StopCancellationTokens { get; } = [];

    public Task<RecordingHandle> StartAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        StartCallCount++;
        StartedPlans.Add(plan);
        CreatedFiles.Add(plan.Output.TemporaryPath);
        _startRequested.TrySetResult();
        return _firstPacketCommitted.Task.WaitAsync(cancellationToken);
    }

    public Task<RecordingStopResult> StopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken)
    {
        StopCallCount++;
        StopCancellationTokens.Add(cancellationToken);
        return _stopCompletion.Task.WaitAsync(cancellationToken);
    }

    public void CompleteStop(RecordingStopResult result) =>
        _stopCompletion.TrySetResult(result);

    public Task WaitUntilStartRequestedAsync() => _startRequested.Task;

    public void CommitFirstPacket(RecordingHandle handle) =>
        _firstPacketCommitted.TrySetResult(handle);
}
