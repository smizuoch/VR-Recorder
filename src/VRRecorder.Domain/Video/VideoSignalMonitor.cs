using VRRecorder.Domain.Timing;

namespace VRRecorder.Domain.Video;

public sealed class VideoSignalMonitor
{
    private static readonly TimeSpan FreshFrameTimeout =
        TimeSpan.FromMilliseconds(1500);
    private MonotonicTimestamp? _lastFreshFrameAt;
    private MonotonicTimestamp? _signalLostAt;

    public void ObserveFreshFrame(VideoFrameObservation frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _lastFreshFrameAt = frame.ReceivedAt;
        _signalLostAt = null;
    }

    public VideoSignalStatus Evaluate(MonotonicTimestamp now)
    {
        if (_lastFreshFrameAt is not { } lastFreshFrameAt)
        {
            throw new InvalidOperationException(
                "Signal monitoring requires at least one fresh frame.");
        }

        var elapsed = now.Elapsed - lastFreshFrameAt.Elapsed;
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "Evaluation time cannot precede the last fresh frame.");
        }

        if (elapsed < FreshFrameTimeout)
        {
            return VideoSignalStatus.Available;
        }

        _signalLostAt ??= now;
        return VideoSignalStatus.SignalLost;
    }
}
