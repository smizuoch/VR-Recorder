using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Ports;

public interface IMonotonicClock
{
    MonotonicTimestamp Now { get; }

    Task DelayUntilAsync(
        MonotonicTimestamp deadline,
        CancellationToken cancellationToken);
}
