using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingFinalizationEventSink
{
    void Publish(RecordingRecoveryReason reason);
}
