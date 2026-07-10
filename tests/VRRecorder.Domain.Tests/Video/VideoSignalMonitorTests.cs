using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Domain.Tests.Video;

public sealed class VideoSignalMonitorTests
{
    [Fact]
    public void FreshBlackFrameKeepsSignalAvailable()
    {
        var monitor = new VideoSignalMonitor();
        var receivedAt = MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(10));

        monitor.ObserveFreshFrame(new VideoFrameObservation(
            receivedAt,
            isBlack: true));
        var status = monitor.Evaluate(
            receivedAt.Add(TimeSpan.FromSeconds(1)));

        Assert.Equal(VideoSignalStatus.Available, status);
    }
}
