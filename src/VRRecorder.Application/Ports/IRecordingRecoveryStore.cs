using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingRecoveryStore
{
    Task<QuarantinedRecording> QuarantineAsync(
        RecoverableRecording recording,
        CancellationToken cancellationToken);
}
