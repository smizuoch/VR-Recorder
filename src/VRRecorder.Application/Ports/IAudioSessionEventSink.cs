using VRRecorder.Application.Audio;

namespace VRRecorder.Application.Ports;

public interface IAudioSessionEventSink
{
    void Publish(AudioSessionWarning warning);

    void Publish(AudioSessionStatus status);
}
