using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Ports;

public interface IVrChatCameraGateway
{
    Task SetModeAsync(
        CameraMode mode,
        CancellationToken cancellationToken);

    Task SetStreamingAsync(
        bool enabled,
        CancellationToken cancellationToken);
}
