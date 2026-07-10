using VRRecorder.Application.Ports;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Infrastructure.Storage;

public sealed class SystemMonotonicClock : IMonotonicClock
{
    private static readonly TimeSpan MaximumTimerDelay =
        TimeSpan.FromMilliseconds(int.MaxValue - 1L);
    private readonly TimeProvider _timeProvider;
    private readonly long _originTimestamp;

    public SystemMonotonicClock()
        : this(TimeProvider.System)
    {
    }

    public SystemMonotonicClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
        _originTimestamp = timeProvider.GetTimestamp();
    }

    public MonotonicTimestamp Now
    {
        get
        {
            var elapsed = _timeProvider.GetElapsedTime(_originTimestamp);
            return MonotonicTimestamp.FromElapsed(
                elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
        }
    }

    public async Task DelayUntilAsync(
        MonotonicTimestamp deadline,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = Now;
            if (deadline.Elapsed <= now.Elapsed)
            {
                return;
            }

            var remaining = deadline.Elapsed - now.Elapsed;
            var delay = remaining > MaximumTimerDelay
                ? MaximumTimerDelay
                : remaining;
            await Task
                .Delay(delay, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
