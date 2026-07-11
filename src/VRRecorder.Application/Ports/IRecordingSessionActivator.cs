using VRRecorder.Application.Recording;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Ports;

public interface IRecordingSessionActivator
{
    void Activate(
        RecordingHandle handle,
        AudioRouting initialAudioRouting,
        CancellationToken sessionLifetimeToken,
        IRecordingSessionCompletionSink? completionSink = null) =>
        Activate(handle, sessionLifetimeToken, completionSink);

    void Activate(
        RecordingHandle handle,
        CancellationToken sessionLifetimeToken,
        IRecordingSessionCompletionSink? completionSink = null);
}
