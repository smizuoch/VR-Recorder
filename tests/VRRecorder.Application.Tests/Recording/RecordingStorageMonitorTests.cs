using VRRecorder.Application.Recording;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingStorageMonitorTests
{
    [Fact]
    public void ConstructorRejectsInvalidEstimateAndNullDependencies()
    {
        var clock = new ControllableMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var storage = new StubStorageSpaceProbe(
            new StorageSpace(StorageCapacityPolicy.MinimumStartBytes));
        var statuses = new FakeRecordingStorageStatusSink();
        var stopRequests = new FakeStopRequestSink();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecordingStorageMonitor(
                0,
                clock,
                storage,
                statuses,
                stopRequests));
        Assert.Throws<ArgumentNullException>(() =>
            new RecordingStorageMonitor(
                1,
                null!,
                storage,
                statuses,
                stopRequests));
        Assert.Throws<ArgumentNullException>(() =>
            new RecordingStorageMonitor(
                1,
                clock,
                null!,
                statuses,
                stopRequests));
        Assert.Throws<ArgumentNullException>(() =>
            new RecordingStorageMonitor(
                1,
                clock,
                storage,
                null!,
                stopRequests));
        Assert.Throws<ArgumentNullException>(() =>
            new RecordingStorageMonitor(
                1,
                clock,
                storage,
                statuses,
                null!));
    }

    [Fact]
    public async Task RunRejectsNullHandleAndOutputPath()
    {
        var firstPacketAt = MonotonicTimestamp.FromElapsed(TimeSpan.Zero);
        var clock = new ControllableMonotonicClock(
            firstPacketAt.Add(StorageCapacityPolicy.MonitorInterval));
        var storage = new StubStorageSpaceProbe(
            new StorageSpace(StorageCapacityPolicy.StopBelowBytes - 1));
        var monitor = new RecordingStorageMonitor(
            estimatedBytesPerSecond: 1,
            clock,
            storage,
            new FakeRecordingStorageStatusSink(),
            new FakeStopRequestSink());
        var handle = new RecordingHandle("session-001", firstPacketAt);
        var outputPath = new OutputPath(Path.GetTempPath());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            monitor.RunAsync(
                null!,
                outputPath,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            monitor.RunAsync(
                handle,
                null!,
                CancellationToken.None));
    }

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
            estimatedBytesPerSecond: 10_000_000,
            clock,
            storage,
            statuses,
            stopRequests);

        var monitoring = monitor.RunAsync(
            handle,
            outputPath,
            CancellationToken.None);
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

    [Fact]
    public async Task WarningContinuesMonitoringUntilStopIsRequired()
    {
        var firstPacketAt = MonotonicTimestamp.FromElapsed(
            TimeSpan.FromSeconds(10));
        var clock = new ControllableMonotonicClock(firstPacketAt);
        var outputPath = new OutputPath(Path.GetTempPath());
        var storage = new SequencedStorageSpaceProbe(
            new StorageSpace(StorageCapacityPolicy.StopBelowBytes),
            new StorageSpace(StorageCapacityPolicy.StopBelowBytes - 1));
        var statuses = new FakeRecordingStorageStatusSink();
        var stopRequests = new FakeStopRequestSink();
        var handle = new RecordingHandle("session-001", firstPacketAt);
        var monitor = new RecordingStorageMonitor(
            estimatedBytesPerSecond: 10_000_000,
            clock,
            storage,
            statuses,
            stopRequests);

        var monitoring = monitor.RunAsync(
            handle,
            outputPath,
            CancellationToken.None);
        await clock.WaitUntilDeadlineRequestedAsync();
        clock.AdvanceBy(StorageCapacityPolicy.MonitorInterval);
        await monitoring;

        Assert.Equal(2, clock.DelayCallCount);
        Assert.Equal(2, storage.CallCount);
        Assert.Equal(
            [
                RecordingStorageState.Warning,
                RecordingStorageState.StopRequired,
            ],
            statuses.Snapshots.Select(snapshot => snapshot.State));
        var request = Assert.Single(stopRequests.Requests);
        Assert.Equal(handle, request.Handle);
        Assert.Equal(RecordingStopReason.DiskLow, request.Reason);
    }

    private sealed class SequencedStorageSpaceProbe(
        params StorageSpace[] spaces) : IStorageSpaceProbe
    {
        private readonly Queue<StorageSpace> _spaces = new(spaces);

        public int CallCount { get; private set; }

        public Task<StorageSpace> MeasureAsync(
            OutputPath outputPath,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(outputPath);
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_spaces.Dequeue());
        }
    }
}
