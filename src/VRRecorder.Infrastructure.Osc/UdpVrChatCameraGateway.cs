using System.Net;
using System.Net.Sockets;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Infrastructure.Osc;

public sealed class UdpVrChatCameraGateway
    : IVrChatCameraGateway, IAsyncDisposable
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _remoteEndpoint;

    public UdpVrChatCameraGateway(IPEndPoint remoteEndpoint)
    {
        ArgumentNullException.ThrowIfNull(remoteEndpoint);
        if (!IPAddress.IsLoopback(remoteEndpoint.Address))
        {
            throw new ArgumentException(
                "The fallback VRChat OSC endpoint must be loopback.",
                nameof(remoteEndpoint));
        }

        if (remoteEndpoint.AddressFamily is not (
                AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            throw new ArgumentException(
                "Only IPv4 and IPv6 OSC endpoints are supported.",
                nameof(remoteEndpoint));
        }

        _remoteEndpoint = new IPEndPoint(
            remoteEndpoint.Address,
            remoteEndpoint.Port);
        _client = new UdpClient(remoteEndpoint.AddressFamily);
        if (remoteEndpoint.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _client.Client.DualMode = false;
        }

        _client.Client.Bind(new IPEndPoint(
            remoteEndpoint.AddressFamily == AddressFamily.InterNetwork
                ? IPAddress.Loopback
                : IPAddress.IPv6Loopback,
            0));
    }

    public Task SetModeAsync(
        CameraMode mode,
        CancellationToken cancellationToken) =>
        SendAsync(OscPacketCodec.EncodeMode(mode), cancellationToken);

    public Task SetStreamingAsync(
        bool enabled,
        CancellationToken cancellationToken) =>
        SendAsync(OscPacketCodec.EncodeStreaming(enabled), cancellationToken);

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task SendAsync(
        byte[] packet,
        CancellationToken cancellationToken)
    {
        var bytesSent = await _client
            .SendAsync(packet, _remoteEndpoint, cancellationToken)
            .ConfigureAwait(false);
        if (bytesSent != packet.Length)
        {
            throw new IOException(
                $"OSC datagram was truncated to {bytesSent} of {packet.Length} bytes.");
        }
    }
}
