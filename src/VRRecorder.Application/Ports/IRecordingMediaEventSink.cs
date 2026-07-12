using VRRecorder.Application.Recording;
using VRRecorder.Application.Audio;

namespace VRRecorder.Application.Ports;

public interface IRecordingMediaEventSink
{
    void Publish(RecordingMediaProfile profile);

    void Publish(RecordingSessionStatistics statistics);

    void Publish(RecordingAvDriftEvent drift)
    {
    }

    void Publish(RecordingEnvironmentSnapshot environment)
    {
    }

    void Publish(RecordingAudioBufferHealthEvent health)
    {
    }
}
