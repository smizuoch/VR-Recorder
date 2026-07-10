using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class AutoStopSchedulerTests
{
    [Fact]
    public async Task ThreeSecondsAfterFirstPacketRequestsStop()
    {
        var clock = new ControllableMonotonicClock();
        var stopRequests = new FakeStopRequestSink();
        var scheduler = new AutoStopScheduler(clock, stopRequests);
        var handle = new RecordingHandle("session-001");

        var scheduling = scheduler.OnFirstPacketCommittedAsync(
            handle,
            RecordingDuration.FromSeconds(3),
            CancellationToken.None);
        var requestedDelay = await clock.WaitUntilDelayRequestedAsync();

        Assert.Equal(TimeSpan.FromSeconds(3), requestedDelay);
        Assert.Empty(stopRequests.RequestedHandles);

        clock.AdvanceRequestedDelay();
        await scheduling;

        var requestedHandle = Assert.Single(stopRequests.RequestedHandles);
        Assert.Equal(handle, requestedHandle);
    }
}
