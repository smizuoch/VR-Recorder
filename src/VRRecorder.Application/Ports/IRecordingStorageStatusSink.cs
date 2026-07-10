using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IRecordingStorageStatusSink
{
    Task PublishAsync(
        RecordingStorageSnapshot snapshot,
        CancellationToken cancellationToken);
}
