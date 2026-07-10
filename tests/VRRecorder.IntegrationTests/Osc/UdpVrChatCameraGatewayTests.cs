using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class UdpVrChatCameraGatewayTests
{
    [Fact]
    [Trait("Scenario", "IT-001")]
    public async Task MissingFirstEchoRetriesOnceAfterTwoHundredMilliseconds()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var fakeVrChat = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)fakeVrChat.Client.LocalEndPoint!;
        await using var gateway = new ConfirmedUdpVrChatCameraGateway(endpoint);
        var stopwatch = Stopwatch.StartNew();

        var write = gateway.SetStreamingAsync(true, timeout.Token);
        var first = await fakeVrChat.ReceiveAsync(timeout.Token);
        var firstReceivedAt = stopwatch.Elapsed;
        var second = await fakeVrChat.ReceiveAsync(timeout.Token);
        var retryDelay = stopwatch.Elapsed - firstReceivedAt;
        await fakeVrChat.SendAsync(
            second.Buffer,
            second.RemoteEndPoint,
            timeout.Token);
        await write;

        Assert.Equal(StreamingTruePacket, first.Buffer);
        Assert.Equal(StreamingTruePacket, second.Buffer);
        Assert.InRange(
            retryDelay,
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        Assert.Equal(0, fakeVrChat.Available);
    }

    [Fact]
    [Trait("Scenario", "IT-001")]
    public async Task AcquireAndRestoreSendOrderedLoopbackDatagrams()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var receiver = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
        await using var gateway = new UdpVrChatCameraGateway(endpoint);
        var controller = new CameraSessionController(
            gateway,
            new InMemoryCameraLeaseStore());
        var snapshot = new CameraSnapshot(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false));

        var lease = await controller.AcquireAsync(snapshot, timeout.Token);

        Assert.Equal(ModeStreamPacket, await ReceiveAsync(receiver, timeout.Token));
        Assert.Equal(StreamingTruePacket, await ReceiveAsync(receiver, timeout.Token));

        await controller.RestoreAsync(lease, timeout.Token);

        Assert.Equal(StreamingFalsePacket, await ReceiveAsync(receiver, timeout.Token));
        Assert.Equal(ModePhotoPacket, await ReceiveAsync(receiver, timeout.Token));
    }

    private static async Task<byte[]> ReceiveAsync(
        UdpClient receiver,
        CancellationToken cancellationToken) =>
        (await receiver.ReceiveAsync(cancellationToken)).Buffer;

    private sealed class InMemoryCameraLeaseStore : ICameraLeaseStore
    {
        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static readonly byte[] ModeStreamPacket = Convert.FromHexString(
        "2F7573657263616D6572612F4D6F6465000000002C69000000000002");

    private static readonly byte[] ModePhotoPacket = Convert.FromHexString(
        "2F7573657263616D6572612F4D6F6465000000002C69000000000001");

    private static readonly byte[] StreamingTruePacket = Convert.FromHexString(
        "2F7573657263616D6572612F53747265616D696E670000002C540000");

    private static readonly byte[] StreamingFalsePacket = Convert.FromHexString(
        "2F7573657263616D6572612F53747265616D696E670000002C460000");
}
