using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public interface INativeRecordingSession
{
    string Id { get; }

    Task AbortAsync(CancellationToken cancellationToken);

    Task<RecordingStopResult> StopAsync(CancellationToken cancellationToken);
}
