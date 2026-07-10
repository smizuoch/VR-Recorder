using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Recording;

public sealed class VideoSignalSupervisorTests
{
    [Fact]
    public async Task SafeStopStatusRequestsStopOnlyOnce()
    {
        var stopRequests = new FakeStopRequestSink();
        var handle = new RecordingHandle(
            "session-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var supervisor = new VideoSignalSupervisor(handle, stopRequests);
        var firstFrameAt = MonotonicTimestamp.FromElapsed(TimeSpan.Zero);
        supervisor.ObserveFreshFrame(new VideoFrameObservation(
            firstFrameAt,
            isBlack: true));

        var signalLostAt = firstFrameAt.Add(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(
            VideoSignalStatus.SignalLost,
            await supervisor.EvaluateAsync(signalLostAt, CancellationToken.None));
        Assert.Empty(stopRequests.RequestedHandles);

        var safeStopAt = signalLostAt.Add(TimeSpan.FromSeconds(5));
        Assert.Equal(
            VideoSignalStatus.SafeStop,
            await supervisor.EvaluateAsync(safeStopAt, CancellationToken.None));
        Assert.Equal(
            VideoSignalStatus.SafeStop,
            await supervisor.EvaluateAsync(
                safeStopAt.Add(TimeSpan.FromSeconds(1)),
                CancellationToken.None));

        var requestedHandle = Assert.Single(stopRequests.RequestedHandles);
        Assert.Equal(handle, requestedHandle);
        Assert.Equal(
            RecordingStopReason.SignalLost,
            Assert.Single(stopRequests.Requests).Reason);
    }
}
