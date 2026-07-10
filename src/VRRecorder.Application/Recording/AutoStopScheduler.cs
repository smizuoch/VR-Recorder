using VRRecorder.Application.Ports;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Recording;

public sealed class AutoStopScheduler
{
    private readonly IMonotonicClock _clock;
    private readonly IStopRequestSink _stopRequests;

    public AutoStopScheduler(
        IMonotonicClock clock,
        IStopRequestSink stopRequests)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(stopRequests);

        _clock = clock;
        _stopRequests = stopRequests;
    }

    public async Task OnFirstPacketCommittedAsync(
        RecordingHandle handle,
        RecordingDuration duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (duration.IsInfinite)
        {
            return;
        }

        var durationValue = TimeSpan.FromSeconds(
            duration.Seconds.GetValueOrDefault());
        var deadline = handle.FirstPacketCommittedAt.Add(durationValue);
        await _clock
            .DelayUntilAsync(deadline, cancellationToken)
            .ConfigureAwait(false);
        await _stopRequests
            .RequestStopAsync(handle, cancellationToken)
            .ConfigureAwait(false);
    }
}
