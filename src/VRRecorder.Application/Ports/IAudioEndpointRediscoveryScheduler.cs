using VRRecorder.Application.Audio;

namespace VRRecorder.Application.Ports;

public interface IAudioEndpointRediscoveryScheduler
{
    void Schedule(AudioEndpointRediscoveryRequest request);
}
