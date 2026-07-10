using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IRecordingSessionCompletionSink
{
    Task CompleteAsync(
        RecordingSessionCompletion completion,
        CancellationToken cancellationToken);
}
