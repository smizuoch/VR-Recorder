using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Ports;

public interface ICameraLeaseIdentitySource
{
    CameraLeaseIdentity Create(string vrChatServiceId);
}
