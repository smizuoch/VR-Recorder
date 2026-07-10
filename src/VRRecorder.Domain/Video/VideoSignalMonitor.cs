using VRRecorder.Domain.Timing;

namespace VRRecorder.Domain.Video;

public sealed class VideoSignalMonitor
{
    private MonotonicTimestamp? _lastFreshFrameAt;

    public void ObserveFreshFrame(VideoFrameObservation frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _lastFreshFrameAt = frame.ReceivedAt;
    }

    public VideoSignalStatus Evaluate(MonotonicTimestamp now)
    {
        _ = now;
        _ = _lastFreshFrameAt;
        return VideoSignalStatus.Available;
    }
}
