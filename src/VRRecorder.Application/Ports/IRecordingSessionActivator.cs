using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IRecordingSessionActivator
{
    void Activate(
        RecordingHandle handle,
        CancellationToken sessionLifetimeToken,
        IRecordingSessionCompletionSink? completionSink = null);
}
