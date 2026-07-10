using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Desktop;

public interface IDesktopRecordingRuntime : IAsyncDisposable
{
    Task ToggleAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(RecordingStopReason reason);
}
