using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IRecordingMediaEventSink
{
    void Publish(RecordingMediaProfile profile);

    void Publish(RecordingSessionStatistics statistics);

    void Publish(RecordingAvDriftEvent drift)
    {
    }
}
