using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingFileValidator
{
    Task<RecordingFileValidation> ValidateAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken);
}
