using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface ISavedRecordingSink
{
    Task PublishAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken);
}
