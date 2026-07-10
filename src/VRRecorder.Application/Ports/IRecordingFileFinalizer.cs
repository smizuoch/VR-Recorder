using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingFileFinalizer
{
    Task<FinalizedRecording> FinalizeAsync(
        PendingRecording recording,
        CancellationToken cancellationToken);
}
