using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Recording;

public sealed class RecordingStorageMonitor
{
    private readonly RecordingHandle _handle;
    private readonly OutputPath _outputPath;
    private readonly long _estimatedBytesPerSecond;
    private readonly IMonotonicClock _clock;
    private readonly IStorageSpaceProbe _storageSpaceProbe;
    private readonly IRecordingStorageStatusSink _statusSink;
    private readonly IStopRequestSink _stopRequests;

    public RecordingStorageMonitor(
        RecordingHandle handle,
        OutputPath outputPath,
        long estimatedBytesPerSecond,
        IMonotonicClock clock,
        IStorageSpaceProbe storageSpaceProbe,
        IRecordingStorageStatusSink statusSink,
        IStopRequestSink stopRequests)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            estimatedBytesPerSecond);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(storageSpaceProbe);
        ArgumentNullException.ThrowIfNull(statusSink);
        ArgumentNullException.ThrowIfNull(stopRequests);

        _handle = handle;
        _outputPath = outputPath;
        _estimatedBytesPerSecond = estimatedBytesPerSecond;
        _clock = clock;
        _storageSpaceProbe = storageSpaceProbe;
        _statusSink = statusSink;
        _stopRequests = stopRequests;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var deadline = _handle.FirstPacketCommittedAt;
        while (true)
        {
            deadline = deadline.Add(StorageCapacityPolicy.MonitorInterval);
            await _clock
                .DelayUntilAsync(deadline, cancellationToken)
                .ConfigureAwait(false);
            var availableSpace = await _storageSpaceProbe
                .MeasureAsync(_outputPath, cancellationToken)
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
                        _handle,
                        RecordingStopReason.DiskLow),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }
    }
}
