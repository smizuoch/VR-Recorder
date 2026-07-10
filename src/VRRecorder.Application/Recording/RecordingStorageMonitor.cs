using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Recording;

public sealed class RecordingStorageMonitor : IRecordingStorageMonitor
{
    private readonly long _estimatedBytesPerSecond;
    private readonly IMonotonicClock _clock;
    private readonly IStorageSpaceProbe _storageSpaceProbe;
    private readonly IRecordingStorageStatusSink _statusSink;
    private readonly IStopRequestSink _stopRequests;

    public RecordingStorageMonitor(
        long estimatedBytesPerSecond,
        IMonotonicClock clock,
        IStorageSpaceProbe storageSpaceProbe,
        IRecordingStorageStatusSink statusSink,
        IStopRequestSink stopRequests)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            estimatedBytesPerSecond);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(storageSpaceProbe);
        ArgumentNullException.ThrowIfNull(statusSink);
        ArgumentNullException.ThrowIfNull(stopRequests);

        _estimatedBytesPerSecond = estimatedBytesPerSecond;
        _clock = clock;
        _storageSpaceProbe = storageSpaceProbe;
        _statusSink = statusSink;
        _stopRequests = stopRequests;
    }

    public async Task RunAsync(
        RecordingHandle handle,
        OutputPath outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(outputPath);

        var deadline = handle.FirstPacketCommittedAt;
        while (true)
        {
            deadline = deadline.Add(StorageCapacityPolicy.MonitorInterval);
            await _clock
                .DelayUntilAsync(deadline, cancellationToken)
                .ConfigureAwait(false);
            var availableSpace = await _storageSpaceProbe
                .MeasureAsync(outputPath, cancellationToken)
                .ConfigureAwait(false);
            var state = StorageCapacityPolicy.Classify(availableSpace);
            var snapshot = new RecordingStorageSnapshot(
                availableSpace,
                state,
                StorageCapacityPolicy.EstimateRemaining(
                    availableSpace,
                    _estimatedBytesPerSecond));
            await _statusSink
                .PublishAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);

            if (state != RecordingStorageState.StopRequired)
            {
                continue;
            }

            await _stopRequests
                .RequestStopAsync(
                    new RecordingStopRequest(
                        handle,
                        RecordingStopReason.DiskLow),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }
    }
}
