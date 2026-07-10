using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Ports;

public interface IVrChatCameraGateway
{
    Task<CameraSnapshot> ReadSnapshotAsync(
        CancellationToken cancellationToken) =>
        Task.FromException<CameraSnapshot>(new NotSupportedException(
            "This VRChat camera gateway cannot read a camera snapshot."));

    Task SetModeAsync(
        CameraMode mode,
        CancellationToken cancellationToken);

    Task SetStreamingAsync(
        bool enabled,
        CancellationToken cancellationToken);
}
