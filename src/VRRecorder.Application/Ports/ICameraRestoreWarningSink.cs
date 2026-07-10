using VRRecorder.Application.Camera;

namespace VRRecorder.Application.Ports;

public interface ICameraRestoreWarningSink
{
    Task PublishAsync(
        CameraRestoreWarning warning,
        CancellationToken cancellationToken);
}
