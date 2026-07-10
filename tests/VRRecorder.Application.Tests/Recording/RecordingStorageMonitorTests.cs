using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingStorageMonitorTests
{
    [Fact]
    public async Task MeasuresAtFiveSecondsThenRequestsDiskLowSafeStop()
    {
        var firstPacketAt = MonotonicTimestamp.FromElapsed(
            TimeSpan.FromSeconds(10));
        var clock = new ControllableMonotonicClock(firstPacketAt);
        var outputPath = new OutputPath(Path.GetTempPath());
        var storage = new StubStorageSpaceProbe(new StorageSpace(
            StorageCapacityPolicy.StopBelowBytes - 1));
        var statuses = new FakeRecordingStorageStatusSink();
        var stopRequests = new FakeStopRequestSink();
        var handle = new RecordingHandle("session-001", firstPacketAt);
        var monitor = new RecordingStorageMonitor(
            handle,
            outputPath,
            estimatedBytesPerSecond: 10_000_000,
            clock,
            storage,
            statuses,
            stopRequests);

        var monitoring = monitor.RunAsync(CancellationToken.None);
        var firstDeadline = await clock.WaitUntilDeadlineRequestedAsync();

        Assert.Equal(
            firstPacketAt.Add(StorageCapacityPolicy.MonitorInterval),
            firstDeadline);
        Assert.Equal(0, storage.CallCount);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(4999));
        Assert.Equal(0, storage.CallCount);
        clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await monitoring;

        var snapshot = Assert.Single(statuses.Snapshots);
        Assert.Equal(RecordingStorageState.StopRequired, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.EstimatedRemaining);
        var request = Assert.Single(stopRequests.Requests);
        Assert.Equal(handle, request.Handle);
        Assert.Equal(RecordingStopReason.DiskLow, request.Reason);
    }
}
