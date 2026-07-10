using VRRecorder.Application.Storage;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingFileReservation
{
    Task<PendingRecording> ReserveAsync(
        OutputPath outputPath,
        RecordingFileDescriptor descriptor,
        CancellationToken cancellationToken);
}
