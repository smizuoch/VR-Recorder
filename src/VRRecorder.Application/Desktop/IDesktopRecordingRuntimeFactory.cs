namespace VRRecorder.Application.Desktop;

public interface IDesktopRecordingRuntimeFactory
{
    Task<IDesktopRecordingRuntime> InitializeAsync(
        CancellationToken cancellationToken);
}
