using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Infrastructure.Storage;

public sealed class SystemCameraLeaseIdentitySource : ICameraLeaseIdentitySource
{
    private readonly IWallClock _wallClock;

    public SystemCameraLeaseIdentitySource(IWallClock wallClock)
    {
        ArgumentNullException.ThrowIfNull(wallClock);
        _wallClock = wallClock;
    }

    public CameraLeaseIdentity Create(string vrChatServiceId) =>
        new(
            Guid.NewGuid().ToString("N"),
            vrChatServiceId,
            Environment.ProcessId,
            _wallClock.LocalNow.ToUniversalTime());
}
