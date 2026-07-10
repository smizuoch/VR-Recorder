using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Ports;

public interface ICameraLeaseOwnerActivityProbe
{
    ValueTask<bool> IsOwnerActiveAsync(
        CameraLease lease,
        CancellationToken cancellationToken);
}
