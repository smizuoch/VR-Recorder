using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public interface INativeRecordingBackend
{
    Task<INativeRecordingSession> OpenAsync(
        RecordingPlan plan,
        NativeRecordingCallbacks callbacks,
        CancellationToken cancellationToken);
}
