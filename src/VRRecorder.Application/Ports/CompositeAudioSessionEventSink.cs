using VRRecorder.Application.Audio;

namespace VRRecorder.Application.Ports;

public sealed class CompositeAudioSessionEventSink : IAudioSessionEventSink
{
    private readonly IAudioSessionEventSink _first;
    private readonly IAudioSessionEventSink _second;

    public CompositeAudioSessionEventSink(
        IAudioSessionEventSink first,
        IAudioSessionEventSink second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        _first = first;
        _second = second;
    }

    public void Publish(AudioSessionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        _first.Publish(warning);
        _second.Publish(warning);
    }

    public void Publish(AudioSessionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _first.Publish(status);
        _second.Publish(status);
    }
}
