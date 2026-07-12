using System.Net;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Osc;

public sealed class ConfirmedUdpVrChatCameraGatewayFactory
    : IVrChatCameraGatewayFactory
{
    private readonly HttpMessageInvoker? _snapshotHttp;
    private readonly IOscOperationEventSink? _events;

    public ConfirmedUdpVrChatCameraGatewayFactory()
    {
    }

    public ConfirmedUdpVrChatCameraGatewayFactory(
        HttpMessageInvoker snapshotHttp)
        : this(snapshotHttp, events: null)
    {
    }

    public ConfirmedUdpVrChatCameraGatewayFactory(
        HttpMessageInvoker snapshotHttp,
        IOscOperationEventSink? events)
    {
        ArgumentNullException.ThrowIfNull(snapshotHttp);
        _snapshotHttp = snapshotHttp;
        _events = events;
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
                snapshotHttpOwner: null,
                _events);
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
                ownedHttp,
                _events);
        }
        catch
        {
            ownedHttp.Dispose();
            throw;
        }
    }
}
