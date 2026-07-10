using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Tests.Camera;

public sealed class CameraSessionControllerTests
{
    [Fact]
    public async Task IdentityAwareAcquirePersistsRichLeaseBeforeCameraWrites()
    {
        var events = new List<string>();
        var gateway = new RecordingCameraGateway(events);
        var leaseStore = new RecordingCameraLeaseStore(events);
        var expectedIdentity = new CameraLeaseIdentity(
            "session-identity",
            "service-selected",
            processId: 1234,
            new DateTimeOffset(2026, 7, 10, 3, 4, 5, TimeSpan.Zero));
        var identities = new FixedCameraLeaseIdentitySource(expectedIdentity);
        var controller = new CameraSessionController(
            gateway,
            leaseStore,
            identities);
        var snapshot = new CameraSnapshot(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false));

        var lease = await controller.AcquireAsync(
            snapshot,
            "service-selected",
            CancellationToken.None);

        Assert.Same(expectedIdentity, lease.Identity);
        Assert.Same(lease, leaseStore.SavedLease);
        Assert.Equal("service-selected", identities.RequestedServiceId);
        Assert.True(lease.IsPersistable);
        Assert.Equal(
            ["lease:save", "mode:Stream", "streaming:true"],
            events);
    }

    [Fact]
    public async Task AcquirePersistsBeforeWritesAndRestoreRunsInReverseOrder()
    {
        var events = new List<string>();
        var gateway = new RecordingCameraGateway(events);
        var leaseStore = new RecordingCameraLeaseStore(events);
        var controller = new CameraSessionController(gateway, leaseStore);
        var snapshot = new CameraSnapshot(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false));

        var lease = await controller.AcquireAsync(
            snapshot,
            CancellationToken.None);

        Assert.Equal(
            ["lease:save", "mode:Stream", "streaming:true"],
            events);

        await controller.RestoreAsync(lease, CancellationToken.None);

        Assert.Equal(
            [
                "lease:save",
                "mode:Stream",
                "streaming:true",
                "streaming:false",
                "mode:Photo",
                "lease:delete",
            ],
            events);
    }

    private sealed class RecordingCameraGateway : IVrChatCameraGateway
    {
        private readonly List<string> _events;

        public RecordingCameraGateway(List<string> events)
        {
            _events = events;
        }

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            _events.Add($"mode:{mode}");
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            _events.Add($"streaming:{enabled.ToString().ToLowerInvariant()}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCameraLeaseStore : ICameraLeaseStore
    {
        private readonly List<string> _events;

        public RecordingCameraLeaseStore(List<string> events)
        {
            _events = events;
        }

        public CameraLease? SavedLease { get; private set; }

        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            SavedLease = lease;
            _events.Add("lease:save");
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            _events.Add("lease:delete");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedCameraLeaseIdentitySource(
        CameraLeaseIdentity identity) : ICameraLeaseIdentitySource
    {
        public string? RequestedServiceId { get; private set; }

        public CameraLeaseIdentity Create(string vrChatServiceId)
        {
            RequestedServiceId = vrChatServiceId;
            return identity;
        }
    }
}
