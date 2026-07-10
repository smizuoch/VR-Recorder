using VRRecorder.Application.Compliance;

namespace VRRecorder.Application.Ports;

public interface IRecordingRightsAcknowledgementStore
{
    Task<RecordingRightsAcknowledgement?> LoadAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        RecordingRightsAcknowledgement acknowledgement,
        CancellationToken cancellationToken);
}
