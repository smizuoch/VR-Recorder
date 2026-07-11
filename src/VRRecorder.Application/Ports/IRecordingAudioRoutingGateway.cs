using VRRecorder.Application.Recording;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Ports;

public interface IRecordingAudioRoutingGateway
{
    Task UpdateAudioRoutingAsync(
        RecordingHandle handle,
        AudioRouting routing,
        CancellationToken cancellationToken);
}
