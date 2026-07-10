using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IStopRequestSink
{
    Task RequestStopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken);
}
