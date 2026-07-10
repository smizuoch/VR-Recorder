using System.Net;
using System.Net.Sockets;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Osc;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Camera;

public sealed class StaleCameraLeaseRecoveryIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-020")]
    public async Task StaleLeaseRestoresExactLoopbackTargetThenDeletesEvidence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var directory = TemporaryDirectory.Create();
        using var selectedOsc = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var otherOsc = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var store = new FileSystemCameraLeaseStore(
            Path.Combine(directory.Path, "camera-lease.json"));
        var selected = Candidate("selected", selectedOsc);
        var other = Candidate("other", otherOsc);
        var lease = Lease("session-stale", selected.ServiceId, processId: 4321);
        await store.SaveAsync(lease, timeout.Token);
        var warnings = new RecordingWarningSink();
        var recovery = new StaleCameraLeaseRecoveryUseCase(
            store,
            new InactiveLeaseOwnerProbe(),
            Connections([other, selected]),
            warnings);

        var recovering = recovery.ExecuteAsync(timeout.Token);
        var streaming = await selectedOsc.ReceiveAsync(timeout.Token);
        Assert.Equal(
            OscPacketCodec.EncodeStreaming(enabled: false),
            streaming.Buffer);
        await selectedOsc.SendAsync(
            streaming.Buffer,
            streaming.RemoteEndPoint,
            timeout.Token);
        var mode = await selectedOsc.ReceiveAsync(timeout.Token);
        Assert.Equal(OscPacketCodec.EncodeMode(CameraMode.Photo), mode.Buffer);
        await selectedOsc.SendAsync(
            mode.Buffer,
            mode.RemoteEndPoint,
            timeout.Token);

        var result = await recovering;

        var restored = Assert.IsType<StaleCameraLeaseRecoveryResult.Restored>(
            result);
        Assert.Equal("session-stale", restored.SessionId);
        Assert.Null(await store.LoadAsync(timeout.Token));
        Assert.Empty(warnings.Warnings);
        Assert.Equal(0, otherOsc.Available);
    }

    [Fact]
    public async Task LiveOwnerLeavesLeaseWithoutDiscoveringOrWriting()
    {
        using var directory = TemporaryDirectory.Create();
        using var store = new FileSystemCameraLeaseStore(
            Path.Combine(directory.Path, "camera-lease.json"));
        var lease = Lease("session-live", "service-live", processId: 1234);
        await store.SaveAsync(lease, CancellationToken.None);
        var recovery = new StaleCameraLeaseRecoveryUseCase(
            store,
            new ActiveLeaseOwnerProbe(),
            new VrChatCameraConnectionUseCase(
                new VrChatTargetResolver(new UnexpectedDiscovery()),
                new UnexpectedGatewayFactory()),
            new RecordingWarningSink());

        var result = await recovery.ExecuteAsync(CancellationToken.None);

        var active = Assert.IsType<
            StaleCameraLeaseRecoveryResult.OwnerStillActive>(result);
        Assert.Equal("session-live", active.SessionId);
        Assert.Equal(
            "session-live",
            (await store.LoadAsync(CancellationToken.None))?.SessionId);
    }

    [Fact]
    public async Task MissingExactTargetLeavesLeaseAndPublishesTypedWarning()
    {
        using var directory = TemporaryDirectory.Create();
        using var otherOsc = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var store = new FileSystemCameraLeaseStore(
            Path.Combine(directory.Path, "camera-lease.json"));
        var lease = Lease("session-stale", "service-missing", processId: 4321);
        await store.SaveAsync(lease, CancellationToken.None);
        var warnings = new RecordingWarningSink();
        var recovery = new StaleCameraLeaseRecoveryUseCase(
            store,
            new InactiveLeaseOwnerProbe(),
            Connections([Candidate("other", otherOsc)]),
            warnings);

        var result = await recovery.ExecuteAsync(CancellationToken.None);

        var failed = Assert.IsType<StaleCameraLeaseRecoveryResult.Failed>(result);
        Assert.Equal("CAMERA_LEASE_TARGET_NOT_FOUND", failed.Code);
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal(
            CameraRestoreWarningReason.StaleLeaseRecovery,
            warning.Reason);
        var failure = Assert.IsType<StaleCameraLeaseRecoveryException>(
            warning.Failure);
        Assert.Equal(failed.Code, failure.Code);
        Assert.Equal(
            "session-stale",
            (await store.LoadAsync(CancellationToken.None))?.SessionId);
        Assert.Equal(0, otherOsc.Available);
    }

    [Fact]
    public async Task RestoreFailureLeavesLeaseForNextRepairAttempt()
    {
        using var directory = TemporaryDirectory.Create();
        using var osc = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var store = new FileSystemCameraLeaseStore(
            Path.Combine(directory.Path, "camera-lease.json"));
        var candidate = Candidate("selected", osc);
        var lease = Lease("session-stale", candidate.ServiceId, processId: 4321);
        await store.SaveAsync(lease, CancellationToken.None);
        var warnings = new RecordingWarningSink();
        var recovery = new StaleCameraLeaseRecoveryUseCase(
            store,
            new InactiveLeaseOwnerProbe(),
            new VrChatCameraConnectionUseCase(
                new VrChatTargetResolver(new FixedDiscovery([candidate])),
                new FixedGatewayFactory(new FailingCameraGateway())),
            warnings);

        var result = await recovery.ExecuteAsync(CancellationToken.None);

        var failed = Assert.IsType<StaleCameraLeaseRecoveryResult.Failed>(result);
        Assert.Equal("CAMERA_LEASE_RESTORE_FAILED", failed.Code);
        Assert.Equal(
            "session-stale",
            (await store.LoadAsync(CancellationToken.None))?.SessionId);
        Assert.Single(warnings.Warnings);
    }

    private static VrChatCameraConnectionUseCase Connections(
        IReadOnlyList<VrChatInstanceCandidate> candidates) =>
        new(
            new VrChatTargetResolver(new FixedDiscovery(candidates)),
            new ConfirmedUdpVrChatCameraGatewayFactory());

    private static VrChatInstanceCandidate Candidate(
        string serviceId,
        UdpClient osc)
    {
        var endpoint = (IPEndPoint)osc.Client.LocalEndPoint!;
        return new VrChatInstanceCandidate(
            serviceId,
            $"VRChat {serviceId}",
            new Uri($"http://127.0.0.1:{endpoint.Port}/"),
            "127.0.0.1",
            endpoint.Port);
    }

    private static CameraLease Lease(
        string sessionId,
        string serviceId,
        int processId) =>
        new(
            sessionId,
            serviceId,
            processId,
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false),
            changedModeByRecorder: true,
            changedStreamingByRecorder: true);

    private sealed class FixedDiscovery(
        IReadOnlyList<VrChatInstanceCandidate> candidates)
        : IVrChatInstanceDiscovery
    {
        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(candidates);
        }
    }

    private sealed class UnexpectedDiscovery : IVrChatInstanceDiscovery
    {
        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Discovery must not run.");
    }

    private sealed class FixedGatewayFactory(IVrChatCameraGateway gateway)
        : IVrChatCameraGatewayFactory
    {
        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate) =>
            gateway;
    }

    private sealed class UnexpectedGatewayFactory : IVrChatCameraGatewayFactory
    {
        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate) =>
            throw new InvalidOperationException("Gateway creation must not run.");
    }

    private sealed class FailingCameraGateway : IVrChatCameraGateway
    {
        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken) =>
            throw new IOException("OSC mode write failed.");

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new IOException("OSC streaming write failed.");
    }

    private sealed class ActiveLeaseOwnerProbe : ICameraLeaseOwnerActivityProbe
    {
        public ValueTask<bool> IsOwnerActiveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class InactiveLeaseOwnerProbe : ICameraLeaseOwnerActivityProbe
    {
        public ValueTask<bool> IsOwnerActiveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }
    }

    private sealed class RecordingWarningSink : ICameraRestoreWarningSink
    {
        public List<CameraRestoreWarning> Warnings { get; } = [];

        public Task PublishAsync(
            CameraRestoreWarning warning,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Warnings.Add(warning);
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-stale-camera-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
