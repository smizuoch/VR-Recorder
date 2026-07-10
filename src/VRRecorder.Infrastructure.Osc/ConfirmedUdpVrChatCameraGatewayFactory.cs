using System.Net;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Osc;

public sealed class ConfirmedUdpVrChatCameraGatewayFactory
    : IVrChatCameraGatewayFactory
{
    private readonly HttpMessageInvoker? _snapshotHttp;

    public ConfirmedUdpVrChatCameraGatewayFactory()
    {
    }

    public ConfirmedUdpVrChatCameraGatewayFactory(
        HttpMessageInvoker snapshotHttp)
    {
        ArgumentNullException.ThrowIfNull(snapshotHttp);
        _snapshotHttp = snapshotHttp;
    }

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

        var remoteEndpoint = new IPEndPoint(address, candidate.OscPort);
        if (_snapshotHttp is not null)
        {
            return new ConfirmedUdpVrChatCameraGateway(
                remoteEndpoint,
                candidate,
                _snapshotHttp,
                snapshotHttpOwner: null);
        }

        var ownedHttp = new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(1),
            UseProxy = false,
        });
        try
        {
            return new ConfirmedUdpVrChatCameraGateway(
                remoteEndpoint,
                candidate,
                ownedHttp,
                ownedHttp);
        }
        catch
        {
            ownedHttp.Dispose();
            throw;
        }
    }
}
