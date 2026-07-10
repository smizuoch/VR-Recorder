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

public sealed class CameraAcquisitionAtomicityTests
{
    [Fact]
    public async Task StreamingEnableFailureRestoresAndDeletesLeaseBeforePipelineStarts()
    {
        var events = new List<string>();
        var expected = new TestCameraEnableException();
        var gateway = new ScriptedCameraGateway(events)
        {
            EnableFailure = expected,
        };
        var leases = new ScriptedCameraLeaseStore(events);
        var warnings = new ScriptedWarningSink();
        var signal = new CountingUnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        using var lifecycle = CreateLifecycle(
            gateway,
            leases,
            warnings,
            signal,
            reservation,
            engine);

        var actual = await Assert.ThrowsAsync<TestCameraEnableException>(
            () => lifecycle.StartAsync(
                Candidate.ServiceId,
                StartCommand(),
                CancellationToken.None));

        Assert.Same(expected, actual);
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
            ],
            events);
        Assert.Equal(1, leases.DeleteCallCount);
        Assert.Empty(warnings.Warnings);
        AssertPipelineNotStarted(signal, reservation, engine);
    }

    [Fact]
    public async Task CallerCancellationDuringStreamingEnableRestoresAndDeletesLease()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var gateway = new ScriptedCameraGateway(events)
        {
            CancelOnEnable = cancellation,
        };
        var leases = new ScriptedCameraLeaseStore(events);
        var warnings = new ScriptedWarningSink();
        var signal = new CountingUnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        using var lifecycle = CreateLifecycle(
            gateway,
            leases,
            warnings,
            signal,
            reservation,
            engine);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => lifecycle.StartAsync(
                Candidate.ServiceId,
                StartCommand(),
                cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
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
            ],
            events);
        Assert.Equal(1, leases.DeleteCallCount);
        Assert.Empty(warnings.Warnings);
        AssertPipelineNotStarted(signal, reservation, engine);
    }

    [Fact]
    public async Task FailedAcquireRestoreKeepsLeaseWarnsAndPreservesEnableFailure()
    {
        var events = new List<string>();
        var expected = new TestCameraEnableException();
        var restoreFailure = new TestCameraRestoreException();
        var gateway = new ScriptedCameraGateway(events)
        {
            EnableFailure = expected,
            RestoreFailure = restoreFailure,
        };
        var leases = new ScriptedCameraLeaseStore(events);
        var warnings = new ScriptedWarningSink();
        var signal = new CountingUnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        using var lifecycle = CreateLifecycle(
            gateway,
            leases,
            warnings,
            signal,
            reservation,
            engine);

        var actual = await Assert.ThrowsAsync<TestCameraEnableException>(
            () => lifecycle.StartAsync(
                Candidate.ServiceId,
                StartCommand(),
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(
            [
                "snapshot:read",
                "lease:save",
                "mode:Stream",
                "streaming:true",
                "streaming:false",
            ],
            events);
        Assert.Equal(0, leases.DeleteCallCount);
        Assert.NotNull(leases.SavedLease);
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal(CameraRestoreWarningReason.StartFailed, warning.Reason);
        Assert.Same(restoreFailure, warning.Failure);
        AssertPipelineNotStarted(signal, reservation, engine);
    }

    [Fact]
    public async Task FailedAcquireRestoreKeepsLeaseWarnsAndPreservesCallerCancellation()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var restoreFailure = new TestCameraRestoreException();
        var gateway = new ScriptedCameraGateway(events)
        {
            CancelOnEnable = cancellation,
            RestoreFailure = restoreFailure,
        };
        var leases = new ScriptedCameraLeaseStore(events);
        var warnings = new ScriptedWarningSink();
        var signal = new CountingUnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        using var lifecycle = CreateLifecycle(
            gateway,
            leases,
            warnings,
            signal,
            reservation,
            engine);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => lifecycle.StartAsync(
                Candidate.ServiceId,
                StartCommand(),
                cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(0, leases.DeleteCallCount);
        Assert.NotNull(leases.SavedLease);
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal(CameraRestoreWarningReason.StartCanceled, warning.Reason);
        Assert.Same(restoreFailure, warning.Failure);
        AssertPipelineNotStarted(signal, reservation, engine);
    }

    [Fact]
    public async Task LeaseSaveFailureNeverDeletesPreexistingEvidence()
    {
        var events = new List<string>();
        var expected = new TestCameraLeaseSaveException();
        var priorEvidence = new object();
        var gateway = new ScriptedCameraGateway(events);
        var leases = new ScriptedCameraLeaseStore(events)
        {
            SaveFailure = expected,
            PreexistingEvidence = priorEvidence,
        };
        var warnings = new ScriptedWarningSink();
        var signal = new CountingUnexpectedVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        using var lifecycle = CreateLifecycle(
            gateway,
            leases,
            warnings,
            signal,
            reservation,
            engine);

        var actual = await Assert.ThrowsAsync<TestCameraLeaseSaveException>(
            () => lifecycle.StartAsync(
                Candidate.ServiceId,
                StartCommand(),
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
        Assert.Equal(["snapshot:read", "lease:save"], events);
        Assert.Equal(0, leases.DeleteCallCount);
        Assert.Same(priorEvidence, leases.PreexistingEvidence);
        Assert.Empty(warnings.Warnings);
        AssertPipelineNotStarted(signal, reservation, engine);
    }

    [Fact]
    public async Task FinalizationCompletesOnceWhenRestoreAndWarningDeliveryFail()
    {
        var events = new List<string>();
        var restoreFailure = new TestCameraRestoreException();
        var gateway = new ScriptedCameraGateway(events)
        {
            RestoreFailure = restoreFailure,
        };
        var leases = new ScriptedCameraLeaseStore(events);
        var warningFailure = new TestCameraWarningDeliveryException();
        var warnings = new ScriptedWarningSink(warningFailure);
        var signal = new ControllableVideoSignalGateway();
        var reservation = new FakeRecordingFileReservation();
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "atomic.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "atomic.mp4"));
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
        using var lifecycle = CreateLifecycle(
            gateway,
            leases,
            warnings,
            signal,
            reservation,
            engine,
            sessions);
        var starting = lifecycle.StartAsync(
            Candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithStableSignal(new StableVideoSignal(320, 180));
        await engine.WaitUntilStartRequestedAsync();
        var handle = new RecordingHandle(
            "atomic-completion",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        engine.CommitFirstPacket(handle);
        var started = Assert.IsType<StartRecordingResult.Started>(
            (await starting).Recording);

        var userStop = sessions.RequestStopAsync(
            new RecordingStopRequest(
                started.Handle,
                RecordingStopReason.UserRequested),
            CancellationToken.None);
        var competingStop = sessions.RequestStopAsync(
            new RecordingStopRequest(
                started.Handle,
                RecordingStopReason.DiskLow),
            CancellationToken.None);
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 90,
            AudioPacketCount: 142));
        await finalizer.WaitUntilRequestedAsync();
        var finalized = new FinalizedRecording(pending.FinalPath);
        finalizer.Complete(finalized);

        await Task.WhenAll(userStop, competingStop);

        Assert.Equal(finalized, Assert.Single(savedRecordings.Recordings));
        Assert.Equal(1, engine.StopCallCount);
        Assert.Equal(1, gateway.RestoreStreamingCallCount);
        Assert.Equal(1, warnings.PublishCallCount);
        Assert.Same(
            restoreFailure,
            Assert.Single(warnings.Warnings).Failure);
        Assert.Equal(0, leases.DeleteCallCount);
        Assert.Equal(RecorderState.Ready, sessions.State);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
    }

    private static readonly VrChatInstanceCandidate Candidate = new(
        "selected-atomicity",
        "VRChat selected-atomicity",
        new Uri("http://127.0.0.1:19000/"),
        "127.0.0.1",
        9000);

    private static RecordingLifecycleController CreateLifecycle(
        IVrChatCameraGateway gateway,
        ICameraLeaseStore leases,
        ICameraRestoreWarningSink warnings,
        IVideoSignalGateway signal,
        IRecordingFileReservation reservation,
        IRecordingEngine engine,
        IRecordingSessionActivator? sessions = null) =>
        new(
            new VrChatCameraConnectionUseCase(
                new VrChatTargetResolver(new FixedDiscovery(Candidate)),
                new FixedGatewayFactory(gateway)),
            leases,
            CreateStartRecording(
                signal,
                reservation,
                engine,
                sessions ?? new FakeRecordingSessionActivator()),
            sessions as IStopRequestSink ?? new FakeStopRequestSink(),
            warnings);

    private static StartRecordingUseCase CreateStartRecording(
        IVideoSignalGateway signal,
        IRecordingFileReservation reservation,
        IRecordingEngine engine,
        IRecordingSessionActivator sessions) =>
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
            sessions,
            new FakeRecordingStorageMonitor(),
            new AutoStopScheduler(
                new ControllableMonotonicClock(
                    MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
                sessions as IStopRequestSink ?? new FakeStopRequestSink()));

    private static StartRecordingCommand StartCommand() =>
        new(
            SelfTimer.FromSeconds(0),
            RecordingDuration.Infinite,
            new OutputPath(Path.GetTempPath()),
            new FrameRate(30));

    private static void AssertPipelineNotStarted(
        CountingUnexpectedVideoSignalGateway signal,
        FakeRecordingFileReservation reservation,
        FakeRecordingEngine engine)
    {
        Assert.Equal(0, signal.CallCount);
        Assert.Equal(0, reservation.CallCount);
        Assert.Equal(0, engine.StartCallCount);
    }

    private sealed class FixedDiscovery(VrChatInstanceCandidate candidate)
        : IVrChatInstanceDiscovery
    {
        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<VrChatInstanceCandidate>>(
                [candidate]);
        }
    }

    private sealed class FixedGatewayFactory(IVrChatCameraGateway gateway)
        : IVrChatCameraGatewayFactory
    {
        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate) =>
            gateway;
    }

    private sealed class ScriptedCameraGateway(List<string> events)
        : IVrChatCameraGateway
    {
        public Exception? EnableFailure { get; init; }

        public Exception? RestoreFailure { get; init; }

        public CancellationTokenSource? CancelOnEnable { get; init; }

        public int RestoreStreamingCallCount { get; private set; }

        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("snapshot:read");
            return Task.FromResult(new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)));
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
            events.Add($"streaming:{enabled.ToString().ToLowerInvariant()}");
            if (enabled)
            {
                CancelOnEnable?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return EnableFailure is null
                    ? Task.CompletedTask
                    : Task.FromException(EnableFailure);
            }

            RestoreStreamingCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return RestoreFailure is null
                ? Task.CompletedTask
                : Task.FromException(RestoreFailure);
        }
    }

    private sealed class ScriptedCameraLeaseStore(List<string> events)
        : ICameraLeaseStore
    {
        public Exception? SaveFailure { get; init; }

        public object? PreexistingEvidence { get; set; }

        public CameraLease? SavedLease { get; private set; }

        public int DeleteCallCount { get; private set; }

        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("lease:save");
            if (SaveFailure is not null)
            {
                return Task.FromException(SaveFailure);
            }

            SavedLease = lease;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCallCount++;
            events.Add("lease:delete");
            SavedLease = null;
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedWarningSink(Exception? failure = null)
        : ICameraRestoreWarningSink
    {
        public List<CameraRestoreWarning> Warnings { get; } = [];

        public int PublishCallCount { get; private set; }

        public Task PublishAsync(
            CameraRestoreWarning warning,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishCallCount++;
            Warnings.Add(warning);
            return failure is null
                ? Task.CompletedTask
                : Task.FromException(failure);
        }
    }

    private sealed class CountingUnexpectedVideoSignalGateway
        : IVideoSignalGateway
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
            throw new InvalidOperationException(
                "The recording signal pipeline must not start.");
        }
    }

    private sealed class TestCameraEnableException : Exception;

    private sealed class TestCameraRestoreException : Exception;

    private sealed class TestCameraLeaseSaveException : Exception;

    private sealed class TestCameraWarningDeliveryException : Exception;
}
