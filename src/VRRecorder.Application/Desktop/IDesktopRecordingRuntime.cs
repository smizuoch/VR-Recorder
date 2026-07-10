using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Desktop;

public interface IDesktopRecordingRuntime
    : IAsyncDisposable,
      IRecorderStatusSource
{
    Task ToggleAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(RecordingStopReason reason);
}
