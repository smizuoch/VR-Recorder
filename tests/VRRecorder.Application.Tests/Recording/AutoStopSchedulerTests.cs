using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class AutoStopSchedulerTests
{
    [Fact]
    public async Task ThreeSecondsAfterFirstPacketRequestsStop()
    {
        var committedAt = MonotonicTimestamp.FromElapsed(
            TimeSpan.FromSeconds(100));
        var clock = new ControllableMonotonicClock(committedAt);
        var stopRequests = new FakeStopRequestSink();
        var scheduler = new AutoStopScheduler(clock, stopRequests);
        var handle = new RecordingHandle("session-001", committedAt);

        var scheduling = scheduler.OnFirstPacketCommittedAsync(
            handle,
            RecordingDuration.FromSeconds(3),
            CancellationToken.None);
        var requestedDeadline = await clock.WaitUntilDeadlineRequestedAsync();

        Assert.Equal(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(103)),
            requestedDeadline);
        Assert.Empty(stopRequests.RequestedHandles);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(2999));
        Assert.Empty(stopRequests.RequestedHandles);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await scheduling;

        var requestedHandle = Assert.Single(stopRequests.RequestedHandles);
        Assert.Equal(handle, requestedHandle);
        Assert.Equal(
            RecordingStopReason.AutoStop,
            Assert.Single(stopRequests.Requests).Reason);

        clock.AdvanceBy(TimeSpan.FromSeconds(10));
        Assert.Single(stopRequests.RequestedHandles);
    }

    [Fact]
    public async Task InfiniteDurationDoesNotScheduleOrRequestStop()
    {
        var committedAt = MonotonicTimestamp.FromElapsed(TimeSpan.Zero);
        var clock = new ControllableMonotonicClock(committedAt);
        var stopRequests = new FakeStopRequestSink();
        var scheduler = new AutoStopScheduler(clock, stopRequests);

        await scheduler.OnFirstPacketCommittedAsync(
            new RecordingHandle("session-001", committedAt),
            RecordingDuration.Infinite,
            CancellationToken.None);

        Assert.Equal(0, clock.DelayCallCount);
        Assert.Empty(stopRequests.RequestedHandles);
    }
}
