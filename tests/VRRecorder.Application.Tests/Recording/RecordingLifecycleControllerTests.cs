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
            startRecording,
            new FakeStopRequestSink());

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

    [Fact]
    public async Task CountdownCancellationRestoresOwnedCameraStateWithoutStartingMedia()
    {
        var events = new List<string>();
        var candidate = Candidate("selected", 9000);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(new RecordingCameraGateway(events)));
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        var startRecording = new StartRecordingUseCase(
            signal,
            countdown,
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
            new RecordingCameraLeaseStore(events),
            startRecording,
            new FakeStopRequestSink());
        using var cancellation = new CancellationTokenSource();
        var start = lifecycle.StartAsync(
            candidate.ServiceId,
            new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)),
            new StartRecordingCommand(
                SelfTimer.FromSeconds(3),
                RecordingDuration.Infinite,
                new OutputPath(Path.GetTempPath()),
                new FrameRate(30)),
            cancellation.Token);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithStableSignal(new StableVideoSignal(320, 180));
        await countdown.WaitUntilRequestedAsync();

        Assert.Equal(
            ["lease:save", "mode:Stream", "streaming:true"],
            events);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
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
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

    [Fact]
    public async Task SignalTimeoutRestoresOwnedCameraStateWithoutCreatingAFile()
    {
        var events = new List<string>();
        var candidate = Candidate("selected", 9000);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(new RecordingCameraGateway(events)));
        var signal = new ControllableVideoSignalGateway();
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
            new RecordingCameraLeaseStore(events),
            startRecording,
            new FakeStopRequestSink());
        var start = lifecycle.StartAsync(
            candidate.ServiceId,
            new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)),
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(Path.GetTempPath()),
                new FrameRate(30)),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();

        signal.CompleteWithTimeout();
        var result = await start;

        Assert.IsType<StartRecordingResult.NoSignal>(result.Recording);
        Assert.Equal(RecorderState.NoSignal, result.State);
        Assert.Equal(RecorderState.NoSignal, lifecycle.State);
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

    private sealed class FixedGatewayFactory(IVrChatCameraGateway gateway)
        : IVrChatCameraGatewayFactory
    {
        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate) =>
            gateway;
    }

    private sealed class RecordingCameraGateway(List<string> events)
        : IVrChatCameraGateway
    {
        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"mode:{mode}");
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"streaming:{enabled.ToString().ToLowerInvariant()}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCameraLeaseStore(List<string> events)
        : ICameraLeaseStore
    {
        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("lease:save");
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("lease:delete");
            return Task.CompletedTask;
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
