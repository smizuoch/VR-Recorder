using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Ports;

public interface ICameraLeaseStore
{
    Task<CameraLease?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CameraLease?>(null);
    }

    Task SaveAsync(
        CameraLease lease,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        CameraLease lease,
        CancellationToken cancellationToken);
}
