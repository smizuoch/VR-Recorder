using VRRecorder.Application.Camera;

namespace VRRecorder.Application.Ports;

public interface IVrChatCameraGatewayFactory
{
    IVrChatCameraGateway Create(VrChatInstanceCandidate candidate);
}
