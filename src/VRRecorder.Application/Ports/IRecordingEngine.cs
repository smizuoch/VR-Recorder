using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IRecordingEngine
{
    Task<RecordingHandle> StartAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken);

    Task<RecordingStopResult> StopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken);
}
