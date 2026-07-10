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

    [Fact]
    public void OnePointFiveSecondsWithoutFreshFrameEntersSignalLost()
    {
        var monitor = new VideoSignalMonitor();
        var receivedAt = MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(10));
        monitor.ObserveFreshFrame(new VideoFrameObservation(
            receivedAt,
            isBlack: false));

        var beforeThreshold = monitor.Evaluate(
            receivedAt.Add(TimeSpan.FromMilliseconds(1499)));
        var atThreshold = monitor.Evaluate(
            receivedAt.Add(TimeSpan.FromMilliseconds(1500)));

        Assert.Equal(VideoSignalStatus.Available, beforeThreshold);
        Assert.Equal(VideoSignalStatus.SignalLost, atThreshold);
    }

    [Fact]
    public void FiveSecondsWithoutRecoveryRequestsSafeStop()
    {
        var monitor = new VideoSignalMonitor();
        var receivedAt = MonotonicTimestamp.FromElapsed(TimeSpan.Zero);
        monitor.ObserveFreshFrame(new VideoFrameObservation(
            receivedAt,
            isBlack: false));
        var signalLostAt = receivedAt.Add(TimeSpan.FromMilliseconds(1500));

        Assert.Equal(
            VideoSignalStatus.SignalLost,
            monitor.Evaluate(signalLostAt));
        Assert.Equal(
            VideoSignalStatus.SignalLost,
            monitor.Evaluate(signalLostAt.Add(TimeSpan.FromMilliseconds(4999))));
        Assert.Equal(
            VideoSignalStatus.SafeStop,
            monitor.Evaluate(signalLostAt.Add(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public void FreshFrameWithinGraceRecoversSignal()
    {
        var monitor = new VideoSignalMonitor();
        var receivedAt = MonotonicTimestamp.FromElapsed(TimeSpan.Zero);
        monitor.ObserveFreshFrame(new VideoFrameObservation(
            receivedAt,
            isBlack: false));
        var signalLostAt = receivedAt.Add(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(
            VideoSignalStatus.SignalLost,
            monitor.Evaluate(signalLostAt));

        var recoveredAt = signalLostAt.Add(TimeSpan.FromMilliseconds(4999));
        monitor.ObserveFreshFrame(new VideoFrameObservation(
            recoveredAt,
            isBlack: true));

        Assert.Equal(
            VideoSignalStatus.Available,
            monitor.Evaluate(recoveredAt));
    }
}
