using VRRecorder.Application.Camera;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecondDesktopRecCancelsConnectionPreparation(
        bool cancelDuringSnapshot)
    {
        var candidate = Candidate("connection-cancel", 9000);
        var discovery = cancelDuringSnapshot
            ? new ControllableDiscovery([candidate])
            : new ControllableDiscovery();
        var gateway = new ControllableSnapshotCameraGateway();
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(discovery),
            new FixedGatewayFactory(gateway));
        var signal = new UnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        var lifecycle = new RecordingLifecycleController(
            connections,
            new InMemoryCameraLeaseStore(),
            CreateStartRecording(signal, reservation, engine),
            new FakeStopRequestSink(),
            new FakeCameraRestoreWarningSink());
        List<RecorderStatusSnapshot> statuses = [];
        using var statusSubscription = lifecycle.Subscribe(statuses.Add);
        await using var runtime = new DesktopRecordingRuntime(
            new FixedStartRequestSource(new DesktopRecordingStartRequest(
                candidate.ServiceId,
                StartCommand())),
            lifecycle,
            new FakeStopRequestSink());

        var first = runtime.ToggleAsync(CancellationToken.None);
        if (cancelDuringSnapshot)
        {
            await gateway.WaitUntilSnapshotRequestedAsync();
        }
        else
        {
            await discovery.WaitUntilRequestedAsync();
        }

        var stateAtSecondRec = lifecycle.State;
        var second = runtime.ToggleAsync(CancellationToken.None);

        Assert.Same(first, second);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(RecorderState.Arming, stateAtSecondRec);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.True(cancelDuringSnapshot
            ? gateway.CancellationObserved
            : discovery.CancellationObserved);
        Assert.Equal(cancelDuringSnapshot ? 1 : 0, gateway.DisposeCallCount);
        Assert.Equal(0, signal.CallCount);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
        AssertStatusSequence(
            statuses,
            RecorderState.Ready,
            RecorderState.Arming,
            RecorderState.Ready);
    }

    [Fact]
    public async Task RetryCanceledDuringDiscoveryRestoresNoSignal()
    {
        var candidate = Candidate("retry-cancel", 9000);
        var discovery = new RetryThenBlockingDiscovery(candidate);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(discovery),
            new FixedGatewayFactory(new SnapshotCameraGateway(
                KnownPhotoSnapshot())));
        var signal = new ControllableVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new InMemoryCameraLeaseStore(),
            CreateStartRecording(signal, reservation, engine),
            new FakeStopRequestSink(),
            new FakeCameraRestoreWarningSink());
        var first = lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithTimeout();
        await first;
        using var cancellation = new CancellationTokenSource();

        var retry = lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            cancellation.Token);
        await discovery.WaitUntilRetryRequestedAsync();
        var stateAtCancel = lifecycle.State;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
        Assert.Equal(RecorderState.Arming, stateAtCancel);
        Assert.Equal(RecorderState.NoSignal, lifecycle.State);
        Assert.True(discovery.RetryCancellationObserved);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

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
                "gateway:dispose",
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
    public async Task UnknownSelectedSnapshotStartsAndRestoresWithoutGuessingMode()
    {
        var candidate = Candidate("selected-snapshot", 9000);
        var events = new List<string>();
        var gateway = new SnapshotCameraGateway(new CameraSnapshot(
            ObservedCameraValue.Unknown<CameraMode>(),
            ObservedCameraValue.Unknown<bool>()), events);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(gateway));
        var signal = new ControllableVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "unknown.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "unknown.mp4"));
        reservation.Complete(pending);
        var engine = new FakeRecordingEngine();
        var sessions = new CapturingRecordingSessionActivator();
        var leases = new RecordingCameraLeaseStore(events);
        using var lifecycle = new RecordingLifecycleController(
            connections,
            leases,
            CreateStartRecording(signal, reservation, engine, sessions),
            new FakeStopRequestSink(),
            new FakeCameraRestoreWarningSink());
        List<RecorderStatusSnapshot> statuses = [];
        using var statusSubscription = lifecycle.Subscribe(statuses.Add);

        var starting = lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithStableSignal(new StableVideoSignal(320, 180));
        await engine.WaitUntilStartRequestedAsync();
        var handle = new RecordingHandle(
            "unknown-snapshot-session",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        engine.CommitFirstPacket(handle);

        var result = await starting;
        Assert.Null(result.SnapshotFailure);
        Assert.IsType<StartRecordingResult.Started>(result.Recording);
        Assert.Equal(RecorderState.Recording, result.State);
        Assert.Equal(RecorderState.Recording, lifecycle.State);
        Assert.IsType<VrChatCameraConnectionResolution.Connected>(
            result.Connection);
        Assert.Equal(1, gateway.ReadCallCount);
        Assert.Equal(2, gateway.WriteCallCount);
        var lease = Assert.IsType<CameraLease>(leases.SavedLease);
        Assert.False(lease.PreviousMode.IsKnown);
        Assert.False(lease.PreviousStreaming.IsKnown);
        Assert.True(lease.ChangedModeByRecorder);
        Assert.True(lease.ChangedStreamingByRecorder);
        Assert.Equal(
            ["snapshot:read", "lease:save", "mode:Stream", "streaming:true"],
            events);

        await Assert.IsAssignableFrom<IRecordingSessionCompletionSink>(
                sessions.CompletionSink)
            .CompleteAsync(
                new RecordingSessionCompletion(
                    handle,
                    RecordingStopReason.UserRequested,
                    RecorderState.Ready),
                CancellationToken.None);

        Assert.Equal(
            [
                "snapshot:read",
                "lease:save",
                "mode:Stream",
                "streaming:true",
                "streaming:false",
                "lease:delete",
            ],
            events);
        Assert.Equal(1, gateway.DisposeCallCount);
        Assert.DoesNotContain("mode:Photo", events);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        AssertStatusSequence(
            statuses,
            RecorderState.Ready,
            RecorderState.Arming,
            RecorderState.Starting,
            RecorderState.Recording,
            RecorderState.Ready);
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
            CreateStartRecording(signal, reservation, engine),
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
        Assert.Equal(1, gateway.DisposeCallCount);
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
        List<RecorderStatusSnapshot> statuses = [];
        using var statusSubscription = lifecycle.Subscribe(statuses.Add);
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
        var stateDuringCountdown = lifecycle.State;

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
                "gateway:dispose",
            ],
            events);
        var warning = Assert.Single(restoreWarnings.Warnings);
        Assert.Equal(CameraRestoreWarningReason.StartCanceled, warning.Reason);
        Assert.IsType<TestCameraRestoreException>(warning.Failure);
        Assert.Equal(RecorderState.Countdown, stateDuringCountdown);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
        AssertStatusSequence(
            statuses,
            RecorderState.Ready,
            RecorderState.Arming,
            RecorderState.Countdown,
            RecorderState.Ready);
    }

    [Theory]
    [InlineData(false, RecorderState.Arming)]
    [InlineData(true, RecorderState.Countdown)]
    public async Task StartCancellationRestoresCameraAndDeletesLeaseEvidence(
        bool cancelDuringCountdown,
        RecorderState expectedCancelState)
    {
        var events = new List<string>();
        var candidate = Candidate("cancel-selected", 9000);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(new RecordingCameraGateway(events)));
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        var restoreWarnings = new FakeCameraRestoreWarningSink();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new RecordingCameraLeaseStore(events),
            CreateStartRecording(
                signal,
                reservation,
                engine,
                countdown: countdown),
            new FakeStopRequestSink(),
            restoreWarnings);
        using var cancellation = new CancellationTokenSource();
        var start = lifecycle.StartAsync(
            candidate.ServiceId,
            new StartRecordingCommand(
                SelfTimer.FromSeconds(cancelDuringCountdown ? 3 : 0),
                RecordingDuration.Infinite,
                new OutputPath(Path.GetTempPath()),
                new FrameRate(30)),
            cancellation.Token);
        await signal.WaitUntilRequestedAsync();
        if (cancelDuringCountdown)
        {
            signal.CompleteWithStableSignal(new StableVideoSignal(320, 180));
            await countdown.WaitUntilRequestedAsync();
        }

        var stateAtCancel = lifecycle.State;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(expectedCancelState, stateAtCancel);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(
            [
                "snapshot:read",
                "lease:save",
                "mode:Stream",
                "streaming:true",
                "streaming:false",
                "mode:Photo",
                "lease:delete",
                "gateway:dispose",
            ],
            events);
        Assert.Empty(restoreWarnings.Warnings);
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
                "gateway:dispose",
            ],
            events);
        var warning = Assert.Single(restoreWarnings.Warnings);
        Assert.Equal(CameraRestoreWarningReason.NoSignal, warning.Reason);
        Assert.IsType<TestCameraRestoreException>(warning.Failure);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

    [Fact]
    public async Task NoSignalCanRetryAndStartWithoutRestartingTheProcess()
    {
        var candidate = Candidate("retry-selected", 9000);
        var camera = new SnapshotCameraGateway(KnownPhotoSnapshot());
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([candidate])),
            new FixedGatewayFactory(camera));
        var signal = new RetryVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        reservation.Complete(new PendingRecording(
            Path.Combine(Path.GetTempPath(), "retry.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "retry.mp4")));
        var engine = new FakeRecordingEngine();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new InMemoryCameraLeaseStore(),
            CreateStartRecording(signal, reservation, engine),
            new FakeStopRequestSink(),
            new FakeCameraRestoreWarningSink());

        var timedOut = await lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);

        Assert.IsType<StartRecordingResult.NoSignal>(timedOut.Recording);
        Assert.Equal(RecorderState.NoSignal, lifecycle.State);

        using var retryTimeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(2));
        var retrying = lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            retryTimeout.Token);
        var engineRequested = engine.WaitUntilStartRequestedAsync();
        Assert.Same(
            engineRequested,
            await Task.WhenAny(retrying, engineRequested));
        engine.CommitFirstPacket(new RecordingHandle(
            "retry-session",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero)));

        var retried = await retrying;
        Assert.IsType<StartRecordingResult.Started>(retried.Recording);
        Assert.Equal(RecorderState.Recording, retried.State);
        Assert.Equal(RecorderState.Recording, lifecycle.State);
        Assert.Equal(2, signal.CallCount);
        Assert.Equal(1, engine.StartCallCount);
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

    private static StartRecordingUseCase CreateStartRecording(
        IVideoSignalGateway signal,
        IRecordingFileReservation reservation,
        IRecordingEngine engine,
        IRecordingSessionActivator? sessionActivator = null,
        ICountdownTimer? countdown = null) =>
        new(
            signal,
            countdown ?? new ControllableCountdownTimer(),
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
            sessionActivator ?? new FakeRecordingSessionActivator(),
            new FakeRecordingStorageMonitor(),
            new AutoStopScheduler(
                new ControllableMonotonicClock(
                    MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
                new FakeStopRequestSink()));

    private static void AssertStatusSequence(
        IReadOnlyList<RecorderStatusSnapshot> statuses,
        params RecorderState[] expectedStates)
    {
        Assert.Equal(expectedStates, statuses.Select(status => status.State));
        Assert.Equal(
            Enumerable.Range(0, expectedStates.Length).Select(value => (long)value),
            statuses.Select(status => status.Revision));
        Assert.All(statuses, status => Assert.Equal(
            RecorderStatusSnapshot.Create(status.Revision, status.State),
            status));
    }

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

    private sealed class ControllableDiscovery
        : IVrChatInstanceDiscovery
    {
        private readonly TaskCompletionSource<IReadOnlyList<
            VrChatInstanceCandidate>> _completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ControllableDiscovery(
            IReadOnlyList<VrChatInstanceCandidate>? completed = null)
        {
            if (completed is not null)
            {
                _completion.SetResult(completed);
            }
        }

        public bool CancellationObserved { get; private set; }

        public async Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            _requested.TrySetResult();
            try
            {
                return await _completion.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public Task WaitUntilRequestedAsync() => _requested.Task;
    }

    private sealed class RetryThenBlockingDiscovery(
        VrChatInstanceCandidate candidate) : IVrChatInstanceDiscovery
    {
        private readonly TaskCompletionSource _retryRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IReadOnlyList<
            VrChatInstanceCandidate>> _retry = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public bool RetryCancellationObserved { get; private set; }

        public async Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return [candidate];
            }

            _retryRequested.TrySetResult();
            try
            {
                return await _retry.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                RetryCancellationObserved = true;
                throw;
            }
        }

        public Task WaitUntilRetryRequestedAsync() => _retryRequested.Task;
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

    private sealed class ControllableSnapshotCameraGateway
        : IVrChatCameraGateway, IAsyncDisposable
    {
        private readonly TaskCompletionSource _requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CameraSnapshot> _snapshot = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        public int DisposeCallCount { get; private set; }

        public async Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            _requested.TrySetResult();
            try
            {
                return await _snapshot.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Camera writes were not expected before snapshot completion.");

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Camera writes were not expected before snapshot completion.");

        public Task WaitUntilSnapshotRequestedAsync() => _requested.Task;

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedStartRequestSource(
        DesktopRecordingStartRequest request)
        : IDesktopRecordingStartRequestSource
    {
        public Task<DesktopRecordingStartRequest> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request);
        }
    }

    private sealed class FailingRestoreCameraGateway(List<string> events)
        : IVrChatCameraGateway, IAsyncDisposable
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

        public ValueTask DisposeAsync()
        {
            events.Add("gateway:dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCameraGateway(List<string> events)
        : IVrChatCameraGateway, IAsyncDisposable
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

        public ValueTask DisposeAsync()
        {
            events.Add("gateway:dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestCameraRestoreException : Exception
    {
    }

    private sealed class TestCameraSnapshotReadException : Exception
    {
    }

    private sealed class SnapshotCameraGateway(
        CameraSnapshot snapshot,
        List<string>? events = null)
        : IVrChatCameraGateway, IAsyncDisposable
    {
        public int ReadCallCount { get; private set; }

        public int WriteCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCallCount++;
            events?.Add("snapshot:read");
            return Task.FromResult(snapshot);
        }

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCallCount++;
            events?.Add($"mode:{mode}");
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCallCount++;
            events?.Add($"streaming:{enabled.ToString().ToLowerInvariant()}");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingRecordingSessionActivator
        : IRecordingSessionActivator
    {
        public IRecordingSessionCompletionSink? CompletionSink { get; private set; }

        public void Activate(
            RecordingHandle handle,
            CancellationToken sessionLifetimeToken,
            IRecordingSessionCompletionSink? completionSink = null) =>
            CompletionSink = completionSink;
    }

    private sealed class FailingSnapshotCameraGateway(Exception failure)
        : IVrChatCameraGateway, IAsyncDisposable
    {
        public int ReadCallCount { get; private set; }

        public int WriteCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

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

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
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

        public Task CaptureBaselineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Signal wait was not expected.");
        }
    }

    private sealed class RetryVideoSignalGateway : IVideoSignalGateway
    {
        public int CallCount { get; private set; }

        public Task CaptureBaselineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return CallCount == 1
                ? Task.FromException<StableVideoSignal>(new TimeoutException())
                : Task.FromResult(new StableVideoSignal(320, 180));
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
