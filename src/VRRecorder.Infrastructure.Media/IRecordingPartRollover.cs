using VRRecorder.Application.Recording;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Infrastructure.Media;

public interface IRecordingPartRollover
{
    Task<RecordingPlan> ReserveNextSoftwarePartAsync(
        RecordingPlan currentPlan,
        int segmentNumber,
        AudioRouting audioRouting,
        CancellationToken cancellationToken);

    Task FinalizeIntermediatePartAsync(
        RecordingStopResult stopped,
        CancellationToken cancellationToken);
}
