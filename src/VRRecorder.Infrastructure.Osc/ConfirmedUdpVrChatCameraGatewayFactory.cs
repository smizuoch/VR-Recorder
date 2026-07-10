using System.Net;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Osc;

public sealed class ConfirmedUdpVrChatCameraGatewayFactory
    : IVrChatCameraGatewayFactory
{
    public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!IPAddress.TryParse(candidate.OscHost, out var address) ||
            !IPAddress.IsLoopback(address))
        {
            throw new ArgumentException(
                "The discovered VRChat OSC host must be a loopback IP address.",
                nameof(candidate));
        }

        return new ConfirmedUdpVrChatCameraGateway(
            new IPEndPoint(address, candidate.OscPort));
    }
}
