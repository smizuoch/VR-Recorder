using VRRecorder.Application.Desktop;

namespace VRRecorder.Application.Ports;

public interface IDesktopRecordingStartRequestSource
{
    Task<DesktopRecordingStartRequest> GetAsync(
        CancellationToken cancellationToken);
}
