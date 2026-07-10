using System.Net;
using System.Net.Sockets;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Infrastructure.Osc;

public sealed class ConfirmedUdpVrChatCameraGateway
    : IVrChatCameraGateway, IAsyncDisposable
{
    private static readonly TimeSpan ConfirmationTimeout =
        TimeSpan.FromMilliseconds(200);
    private readonly UdpClient _client;
    private readonly IPEndPoint _remoteEndpoint;

    public ConfirmedUdpVrChatCameraGateway(IPEndPoint remoteEndpoint)
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
        SendWithConfirmationAsync(
            OscPacketCodec.EncodeMode(mode),
            cancellationToken);

    public Task SetStreamingAsync(
        bool enabled,
        CancellationToken cancellationToken) =>
        SendWithConfirmationAsync(
            OscPacketCodec.EncodeStreaming(enabled),
            cancellationToken);

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task SendWithConfirmationAsync(
        byte[] packet,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await _client
                .SendAsync(packet, _remoteEndpoint, cancellationToken)
                .ConfigureAwait(false);
            if (await WaitForEchoAsync(packet, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }
        }

        throw new CameraWriteConfirmationException();
    }

    private async Task<bool> WaitForEchoAsync(
        byte[] expectedPacket,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ConfirmationTimeout);
        try
        {
            while (true)
            {
                UdpReceiveResult received;
                try
                {
                    received = await _client
                        .ReceiveAsync(timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (SocketException exception) when (
                    OperatingSystem.IsWindows() &&
                    exception.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue;
                }

                if (received.RemoteEndPoint.Equals(_remoteEndpoint) &&
                    received.Buffer.AsSpan().SequenceEqual(expectedPacket))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
