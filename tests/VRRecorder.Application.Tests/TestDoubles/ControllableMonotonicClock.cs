using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class ControllableMonotonicClock : IMonotonicClock
{
    private readonly TaskCompletionSource<TimeSpan> _delayRequested = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _delayCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task DelayAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        _delayRequested.TrySetResult(duration);
        return _delayCompleted.Task.WaitAsync(cancellationToken);
    }

    public Task<TimeSpan> WaitUntilDelayRequestedAsync() => _delayRequested.Task;

    public void AdvanceRequestedDelay() => _delayCompleted.TrySetResult();
}
