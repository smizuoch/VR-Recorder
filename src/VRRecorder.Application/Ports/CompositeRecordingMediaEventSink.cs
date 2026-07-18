using VRRecorder.Application.Audio;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public sealed class CompositeRecordingMediaEventSink
    : IRecordingMediaEventSink
{
    private readonly IRecordingMediaEventSink _first;
    private readonly IRecordingMediaEventSink _second;

    public CompositeRecordingMediaEventSink(
        IRecordingMediaEventSink first,
        IRecordingMediaEventSink second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        _first = first;
        _second = second;
    }

    public void Publish(RecordingMediaProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _first.Publish(profile);
        _second.Publish(profile);
    }

    public void Publish(RecordingSessionStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        _first.Publish(statistics);
        _second.Publish(statistics);
    }

    public void Publish(RecordingAvDriftEvent drift)
    {
        ArgumentNullException.ThrowIfNull(drift);
        _first.Publish(drift);
        _second.Publish(drift);
    }

    public void Publish(RecordingEnvironmentSnapshot environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _first.Publish(environment);
        _second.Publish(environment);
    }

    public void Publish(RecordingAudioBufferHealthEvent health)
    {
        ArgumentNullException.ThrowIfNull(health);
        _first.Publish(health);
        _second.Publish(health);
    }
}
