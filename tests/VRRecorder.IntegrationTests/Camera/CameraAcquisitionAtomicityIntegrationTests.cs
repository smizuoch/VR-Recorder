using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Camera;

public sealed class CameraAcquisitionAtomicityIntegrationTests
{
    [Fact]
    public async Task PersistedLeaseIsRolledBackAfterPartialCameraAcquisition()
    {
        using var directory = TemporaryDirectory.Create();
        var leasePath = Path.Combine(directory.Path, "camera-lease.json");
        using var store = new FileSystemCameraLeaseStore(leasePath);
        var expected = new TestStreamingEnableException();
        var events = new List<string>();
        var gateway = new PartiallyFailingCameraGateway(
            leasePath,
            expected,
            events);
        var controller = new CameraSessionController(
            gateway,
            store,
            new FixedIdentitySource());
        var snapshot = new CameraSnapshot(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false));

        var actual = await Assert.ThrowsAsync<TestStreamingEnableException>(
            () => controller.AcquireAsync(
                snapshot,
                "selected-service",
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(
            [
                "mode:Stream",
                "streaming:true",
                "streaming:false",
                "mode:Photo",
            ],
            events);
        Assert.Null(await store.LoadAsync(CancellationToken.None));
        Assert.False(File.Exists(leasePath));
    }

    private sealed class PartiallyFailingCameraGateway(
        string leasePath,
        Exception enableFailure,
        List<string> events) : IVrChatCameraGateway
    {
        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(
                File.Exists(leasePath),
                "The ownership lease must be durable before camera writes.");
            events.Add($"mode:{mode}");
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(
                File.Exists(leasePath),
                "The ownership lease must remain durable during camera writes.");
            events.Add($"streaming:{enabled.ToString().ToLowerInvariant()}");
            return enabled
                ? Task.FromException(enableFailure)
                : Task.CompletedTask;
        }
    }

    private sealed class FixedIdentitySource : ICameraLeaseIdentitySource
    {
        public CameraLeaseIdentity Create(string vrChatServiceId) =>
            new(
                "atomic-acquire-session",
                vrChatServiceId,
                processId: 1234,
                new DateTimeOffset(2026, 7, 10, 3, 4, 5, TimeSpan.Zero));
    }

    private sealed class TestStreamingEnableException : Exception;

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
                $"vr-recorder-camera-acquire-{Guid.NewGuid():N}");
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
