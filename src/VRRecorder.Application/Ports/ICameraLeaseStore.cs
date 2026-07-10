using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Ports;

public interface ICameraLeaseStore
{
    Task SaveAsync(
        CameraLease lease,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        CameraLease lease,
        CancellationToken cancellationToken);
}
