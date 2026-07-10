namespace VRRecorder.Application.Desktop;

public interface IDesktopRecordingRuntime : IAsyncDisposable
{
    Task ToggleAsync(CancellationToken cancellationToken);
}
