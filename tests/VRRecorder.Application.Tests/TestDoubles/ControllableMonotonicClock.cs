using VRRecorder.Application.Ports;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class ControllableMonotonicClock : IMonotonicClock
{
    private readonly TaskCompletionSource<MonotonicTimestamp> _deadlineRequested = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _deadlineReached = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private MonotonicTimestamp? _deadline;

    public ControllableMonotonicClock(MonotonicTimestamp initialNow)
    {
        Now = initialNow;
    }

    public int DelayCallCount { get; private set; }

    public MonotonicTimestamp Now { get; private set; }

    public Task DelayUntilAsync(
        MonotonicTimestamp deadline,
        CancellationToken cancellationToken)
    {
        DelayCallCount++;
        _deadline = deadline;
        _deadlineRequested.TrySetResult(deadline);
        return deadline.Elapsed <= Now.Elapsed
            ? Task.CompletedTask
            : _deadlineReached.Task.WaitAsync(cancellationToken);
    }

    public Task<MonotonicTimestamp> WaitUntilDeadlineRequestedAsync() =>
        _deadlineRequested.Task;

    public void AdvanceBy(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "A monotonic clock cannot move backwards.");
        }

        Now = Now.Add(duration);
        if (_deadline is { } deadline && Now.Elapsed >= deadline.Elapsed)
        {
            _deadlineReached.TrySetResult();
        }
    }
}
