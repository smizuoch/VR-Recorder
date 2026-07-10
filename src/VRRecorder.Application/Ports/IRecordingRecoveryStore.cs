using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingRecoveryStore
{
    Task QuarantineAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken);
}
