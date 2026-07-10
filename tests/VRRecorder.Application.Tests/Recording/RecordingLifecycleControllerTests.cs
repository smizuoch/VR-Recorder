using VRRecorder.Application.Camera;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Camera;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingLifecycleControllerTests
{
    [Fact]
    public async Task MultipleTargetsWithoutExactSelectionNeverStartRecording()
    {
        var first = Candidate("service-a", 9000);
        var second = Candidate("service-b", 9010);
        var gateways = new CapturingGatewayFactory();
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([second, first])),
            gateways);
        var signal = new UnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        var startRecording = new StartRecordingUseCase(
            signal,
            new ControllableCountdownTimer(),
            reservation,
            new FixedWallClock(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new StubStorageSpaceProbe(new StorageSpace(
                StorageCapacityPolicy.MinimumStartBytes)),
            new EncoderSelector(new ScriptedEncoderProbe(
                (EncoderKind.MediaFoundationSoftware,
                    EncoderProbeResult.PacketProduced))),
            engine,
            new FakeRecordingSessionActivator(),
            new FakeRecordingStorageMonitor(),
            new AutoStopScheduler(
                new ControllableMonotonicClock(
                    MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
                new FakeStopRequestSink()));
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new InMemoryCameraLeaseStore(),
            startRecording);

        var result = await lifecycle.StartAsync(
            selectedServiceId: null,
            new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)),
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(Path.GetTempPath()),
                new FrameRate(30)),
            CancellationToken.None);

        var selection = Assert.IsType<
            VrChatCameraConnectionResolution.SelectionRequired>(
                result.Connection);
        Assert.Equal([first, second], selection.Candidates);
        Assert.Equal(RecorderState.Ready, result.State);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Null(result.Recording);
        Assert.Empty(gateways.CreatedFor);
        Assert.Equal(0, signal.CallCount);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

    private static VrChatInstanceCandidate Candidate(
        string serviceId,
        int oscPort) =>
        new(
            serviceId,
            $"VRChat {serviceId}",
            new Uri($"http://127.0.0.1:{oscPort + 1000}/"),
            "127.0.0.1",
            oscPort);

    private sealed class StubDiscovery(
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

    private sealed class CapturingGatewayFactory : IVrChatCameraGatewayFactory
    {
        public List<VrChatInstanceCandidate> CreatedFor { get; } = [];

        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate)
        {
            CreatedFor.Add(candidate);
            return new UnexpectedCameraGateway();
        }
    }

    private sealed class UnexpectedCameraGateway : IVrChatCameraGateway
    {
        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Camera writes were not expected.");

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Camera writes were not expected.");
    }

    private sealed class UnexpectedVideoSignalGateway : IVideoSignalGateway
    {
        public int CallCount { get; private set; }

        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Signal wait was not expected.");
        }
    }

    private sealed class InMemoryCameraLeaseStore : ICameraLeaseStore
    {
        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
