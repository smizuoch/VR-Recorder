using VRRecorder.Application.Diagnostics;

namespace VRRecorder.Application.Ports;

public interface IOscOperationEventSink
{
    void Publish(OscOperationEvent operation);
}
