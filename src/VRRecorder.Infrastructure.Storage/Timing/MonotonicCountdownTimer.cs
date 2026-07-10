using VRRecorder.Application.Ports;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Infrastructure.Storage;

public sealed class MonotonicCountdownTimer : ICountdownTimer
{
    private readonly IMonotonicClock _clock;

    public MonotonicCountdownTimer(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
    }

    public Task WaitAsync(
        SelfTimer timer,
        CancellationToken cancellationToken)
    {
        if (!timer.IsEnabled)
        {
            return Task.CompletedTask;
        }

        var now = _clock.Now;
        var duration = TimeSpan.FromSeconds(timer.Seconds);
        var availableTicks = TimeSpan.MaxValue.Ticks - now.Elapsed.Ticks;
        var deadlineElapsed = duration.Ticks > availableTicks
            ? TimeSpan.MaxValue
            : now.Elapsed + duration;
        var deadline = MonotonicTimestamp.FromElapsed(deadlineElapsed);
        return _clock.DelayUntilAsync(deadline, cancellationToken);
    }
}
