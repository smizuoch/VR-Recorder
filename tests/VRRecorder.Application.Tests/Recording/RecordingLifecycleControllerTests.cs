using VRRecorder.Application.Camera;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
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
    public async Task UserStopAndCompetingRequestRestoreOwnedCameraOnceAfterSaved()
    {
        var events = new List<string>();
        var candidate = Candidate("selected", 9000);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(new RecordingCameraGateway(events)));
        var signal = new ControllableVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "take.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "take.mp4"));
        reservation.Complete(pending);
        var engine = new FakeRecordingEngine();
        var finalizer = new ControllableRecordingFileFinalizer();
        var savedRecordings = new FakeSavedRecordingSink();
        var sessions = new ActiveRecordingSessionCoordinator(
            engine,
            new RecordingFileFinalizationUseCase(
                finalizer,
                new StubRecordingFileValidator(RecordingFileValidation.Valid),
                new FakeRecordingRecoveryStore(),
                savedRecordings));
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
            sessions,
            new FakeRecordingStorageMonitor(),
            new AutoStopScheduler(
                new ControllableMonotonicClock(
                    MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
                sessions));
        var restoreWarnings = new FakeCameraRestoreWarningSink();
        var leaseStore = new RecordingCameraLeaseStore(events);
        var leaseIdentity = new CameraLeaseIdentity(
            "camera-session-001",
            candidate.ServiceId,
            processId: 1234,
            new DateTimeOffset(2026, 7, 10, 3, 4, 5, TimeSpan.Zero));
        using var lifecycle = new RecordingLifecycleController(
            connections,
            leaseStore,
            startRecording,
            sessions,
            restoreWarnings,
            new FixedLeaseIdentitySource(leaseIdentity));
        var starting = lifecycle.StartAsync(
            candidate.ServiceId,
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(Path.GetTempPath()),
                new FrameRate(30)),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithStableSignal(new StableVideoSignal(320, 180));
        await engine.WaitUntilStartRequestedAsync();
        var handle = new RecordingHandle(
            "session-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        engine.CommitFirstPacket(handle);
        var started = Assert.IsType<StartRecordingResult.Started>(
            (await starting).Recording);
        Assert.Same(leaseIdentity, leaseStore.SavedLease?.Identity);

        var userStop = sessions.RequestStopAsync(
            new RecordingStopRequest(
                started.Handle,
                RecordingStopReason.UserRequested),
            CancellationToken.None);
        var competingDiskLow = sessions.RequestStopAsync(
            new RecordingStopRequest(
                started.Handle,
                RecordingStopReason.DiskLow),
            CancellationToken.None);

        Assert.Equal(1, engine.StopCallCount);
        Assert.Equal(RecordingStopReason.UserRequested, sessions.StopReason);
        Assert.Equal(
            ["snapshot:read", "lease:save", "mode:Stream", "streaming:true"],
            events);
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 90,
            AudioPacketCount: 142));
        await finalizer.WaitUntilRequestedAsync();
        Assert.Equal(
            ["snapshot:read", "lease:save", "mode:Stream", "streaming:true"],
            events);

        var finalized = new FinalizedRecording(pending.FinalPath);
        finalizer.Complete(finalized);
        await Task.WhenAll(userStop, competingDiskLow);

        Assert.Equal(
            [
                "snapshot:read",
                "lease:save",
                "mode:Stream",
                "streaming:true",
                "streaming:false",
                "mode:Photo",
                "lease:delete",
            ],
            events);
        Assert.Equal(1, engine.StopCallCount);
        Assert.Equal(finalized, Assert.Single(savedRecordings.Recordings));
        Assert.Empty(restoreWarnings.Warnings);
        Assert.Equal(RecorderState.Ready, sessions.State);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
    }

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
            new FakeStopRequestSink(),
            new FakeCameraRestoreWarningSink());

        var result = await lifecycle.StartAsync(
            selectedServiceId: null,
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
    public async Task IncompleteSelectedSnapshotFailsBeforeLeaseOrRecordingWork()
    {
        var candidate = Candidate("selected-snapshot", 9000);
        var gateway = new SnapshotCameraGateway(new CameraSnapshot(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Unknown<bool>()));
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(gateway));
        var signal = new UnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        var leaseEvents = new List<string>();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new RecordingCameraLeaseStore(leaseEvents),
            CreateUnexpectedStartRecording(signal, reservation, engine),
            new FakeStopRequestSink(),
            new FakeCameraRestoreWarningSink());

        var result = await lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);

        var failure = Assert.IsType<CameraSnapshotStartFailure>(
            result.SnapshotFailure);
        Assert.Equal(CameraSnapshotStartFailureKind.Incomplete, failure.Kind);
        Assert.Equal(candidate.ServiceId, failure.VrChatServiceId);
        Assert.Null(failure.Failure);
        Assert.Equal(RecorderState.Ready, result.State);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.IsType<VrChatCameraConnectionResolution.Connected>(
            result.Connection);
        Assert.Null(result.Recording);
        Assert.Equal(1, gateway.ReadCallCount);
        Assert.Equal(0, gateway.WriteCallCount);
        Assert.Empty(leaseEvents);
        Assert.Equal(0, signal.CallCount);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

    [Fact]
    public async Task SelectedSnapshotReadFailureReturnsTypedFailureWithoutFallback()
    {
        var candidate = Candidate("selected-read-failure", 9000);
        var expectedFailure = new TestCameraSnapshotReadException();
        var gateway = new FailingSnapshotCameraGateway(expectedFailure);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(gateway));
        var signal = new UnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        var leaseEvents = new List<string>();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new RecordingCameraLeaseStore(leaseEvents),
            CreateUnexpectedStartRecording(signal, reservation, engine),
            new FakeStopRequestSink(),
            new FakeCameraRestoreWarningSink());

        var result = await lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);

        var failure = Assert.IsType<CameraSnapshotStartFailure>(
            result.SnapshotFailure);
        Assert.Equal(CameraSnapshotStartFailureKind.ReadFailed, failure.Kind);
        Assert.Equal(candidate.ServiceId, failure.VrChatServiceId);
        Assert.Same(expectedFailure, failure.Failure);
        Assert.Equal(RecorderState.Ready, result.State);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Null(result.Recording);
        Assert.Equal(1, gateway.ReadCallCount);
        Assert.Equal(0, gateway.WriteCallCount);
        Assert.Empty(leaseEvents);
        Assert.Equal(0, signal.CallCount);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

    [Fact]
    public async Task RestoreFailurePreservesCountdownCancellationAndWarns()
    {
        var events = new List<string>();
        var candidate = Candidate("selected", 9000);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(new FailingRestoreCameraGateway(events)));
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
        var restoreWarnings = new FakeCameraRestoreWarningSink();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new RecordingCameraLeaseStore(events),
            startRecording,
            new FakeStopRequestSink(),
            restoreWarnings);
        using var cancellation = new CancellationTokenSource();
        var start = lifecycle.StartAsync(
            candidate.ServiceId,
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
            ["snapshot:read", "lease:save", "mode:Stream", "streaming:true"],
            events);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(
            [
                "snapshot:read",
                "lease:save",
                "mode:Stream",
                "streaming:true",
                "streaming:false",
            ],
            events);
        var warning = Assert.Single(restoreWarnings.Warnings);
        Assert.Equal(CameraRestoreWarningReason.StartCanceled, warning.Reason);
        Assert.IsType<TestCameraRestoreException>(warning.Failure);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

    [Fact]
    public async Task RestoreFailurePreservesNoSignalResultAndWarns()
    {
        var events = new List<string>();
        var candidate = Candidate("selected", 9000);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(new FailingRestoreCameraGateway(events)));
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
        var restoreWarnings = new FakeCameraRestoreWarningSink();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new RecordingCameraLeaseStore(events),
            startRecording,
            new FakeStopRequestSink(),
            restoreWarnings);
        var start = lifecycle.StartAsync(
            candidate.ServiceId,
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
                "snapshot:read",
                "lease:save",
                "mode:Stream",
                "streaming:true",
                "streaming:false",
            ],
            events);
        var warning = Assert.Single(restoreWarnings.Warnings);
        Assert.Equal(CameraRestoreWarningReason.NoSignal, warning.Reason);
        Assert.IsType<TestCameraRestoreException>(warning.Failure);
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

    private static StartRecordingCommand StartCommand() =>
        new(
            SelfTimer.FromSeconds(0),
            RecordingDuration.Infinite,
            new OutputPath(Path.GetTempPath()),
            new FrameRate(30));

    private static StartRecordingUseCase CreateUnexpectedStartRecording(
        IVideoSignalGateway signal,
        IRecordingFileReservation reservation,
        IRecordingEngine engine) =>
        new(
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

    private sealed class FailingRestoreCameraGateway(List<string> events)
        : IVrChatCameraGateway
    {
        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("snapshot:read");
            return Task.FromResult(KnownPhotoSnapshot());
        }

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
            if (!enabled)
            {
                throw new TestCameraRestoreException();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCameraGateway(List<string> events)
        : IVrChatCameraGateway
    {
        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("snapshot:read");
            return Task.FromResult(KnownPhotoSnapshot());
        }

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

    private sealed class TestCameraRestoreException : Exception
    {
    }

    private sealed class TestCameraSnapshotReadException : Exception
    {
    }

    private sealed class SnapshotCameraGateway(CameraSnapshot snapshot)
        : IVrChatCameraGateway
    {
        public int ReadCallCount { get; private set; }

        public int WriteCallCount { get; private set; }

        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCallCount++;
            return Task.FromResult(snapshot);
        }

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            throw new InvalidOperationException("Camera writes were not expected.");
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            throw new InvalidOperationException("Camera writes were not expected.");
        }
    }

    private sealed class FailingSnapshotCameraGateway(Exception failure)
        : IVrChatCameraGateway
    {
        public int ReadCallCount { get; private set; }

        public int WriteCallCount { get; private set; }

        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCallCount++;
            return Task.FromException<CameraSnapshot>(failure);
        }

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            throw new InvalidOperationException("Camera writes were not expected.");
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            throw new InvalidOperationException("Camera writes were not expected.");
        }
    }

    private sealed class RecordingCameraLeaseStore(List<string> events)
        : ICameraLeaseStore
    {
        public CameraLease? SavedLease { get; private set; }

        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedLease = lease;
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

    private sealed class FixedLeaseIdentitySource(CameraLeaseIdentity identity)
        : ICameraLeaseIdentitySource
    {
        public CameraLeaseIdentity Create(string vrChatServiceId)
        {
            Assert.Equal(identity.VrChatServiceId, vrChatServiceId);
            return identity;
        }
    }

    private sealed class UnexpectedCameraGateway : IVrChatCameraGateway
    {
        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Camera reads were not expected.");

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

    private static CameraSnapshot KnownPhotoSnapshot() =>
        new(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false));
}
