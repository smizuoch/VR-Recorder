using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class ActiveRecordingSessionCoordinatorTests
{
    [Fact]
    public async Task ActiveAudioCommandUpdatesRoutingAndCommittedControlState()
    {
        var gateway = new CapturingAudioRoutingGateway();
        var coordinator = CreateCoordinator(
            new FakeRecordingEngine(),
            gateway);
        var handle = new RecordingHandle(
            "session-audio-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        coordinator.Activate(
            handle,
            AudioRouting.DesktopOnly,
            CancellationToken.None);
        IActiveRecordingAudioCommands audioCommands = coordinator;

        var updated = await audioCommands.ExecuteAudioCommandAsync(
            RecordingAudioCommand.ToggleMicrophone,
            CancellationToken.None);

        Assert.Equal(
            [(handle, AudioRouting.Mixed)],
            gateway.Updates);
        Assert.Equal(AudioRouting.Mixed, updated.EffectiveRouting);
        Assert.Equal(updated, audioCommands.CurrentAudioControlState);
    }

    [Fact]
    public async Task ConcurrentAudioCommandsAreAppliedInAcceptanceOrder()
    {
        var gateway = new CapturingAudioRoutingGateway
        {
            BlockFirstUpdate = true,
        };
        var coordinator = CreateCoordinator(
            new FakeRecordingEngine(),
            gateway);
        var handle = new RecordingHandle(
            "session-audio-fifo",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        coordinator.Activate(
            handle,
            AudioRouting.Mixed,
            CancellationToken.None);

        var microphoneOff = coordinator.ExecuteAudioCommandAsync(
            RecordingAudioCommand.ToggleMicrophone,
            CancellationToken.None);
        await gateway.WaitUntilFirstUpdateStartedAsync();
        var muted = coordinator.ExecuteAudioCommandAsync(
            RecordingAudioCommand.ToggleMuteAll,
            CancellationToken.None);

        Assert.Equal(
            [(handle, AudioRouting.DesktopOnly)],
            gateway.Updates);
        Assert.False(muted.IsCompleted);

        gateway.CompleteFirstUpdate();
        var microphoneOffState = await microphoneOff;
        var mutedState = await muted;

        Assert.Equal(AudioRouting.DesktopOnly, microphoneOffState.EffectiveRouting);
        Assert.False(mutedState.MicrophoneIncluded);
        Assert.True(mutedState.MuteAll);
        Assert.Equal(AudioRouting.Muted, mutedState.EffectiveRouting);
        Assert.Equal(
            [
                (handle, AudioRouting.DesktopOnly),
                (handle, AudioRouting.Muted),
            ],
            gateway.Updates);
        Assert.Equal(mutedState, coordinator.CurrentAudioControlState);
    }

    [Fact]
    public async Task StopWaitsForAcceptedAudioUpdateAndRejectsLaterCommands()
    {
        var engine = new FakeRecordingEngine();
        var finalizer = new ControllableRecordingFileFinalizer();
        var finalization = new RecordingFileFinalizationUseCase(
            finalizer,
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            new FakeRecordingRecoveryStore(),
            new FakeSavedRecordingSink());
        var gateway = new CapturingAudioRoutingGateway
        {
            BlockFirstUpdate = true,
        };
        var coordinator = new ActiveRecordingSessionCoordinator(
            engine,
            finalization,
            gateway);
        var handle = new RecordingHandle(
            "session-audio-stop",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        coordinator.Activate(
            handle,
            AudioRouting.Mixed,
            CancellationToken.None);
        var acceptedUpdate = coordinator.ExecuteAudioCommandAsync(
            RecordingAudioCommand.ToggleMicrophone,
            CancellationToken.None);
        await gateway.WaitUntilFirstUpdateStartedAsync();

        var stopping = coordinator.RequestStopAsync(
            new RecordingStopRequest(
                handle,
                RecordingStopReason.UserRequested),
            CancellationToken.None);

        Assert.Equal(RecorderState.Stopping, coordinator.State);
        Assert.Equal(0, engine.StopCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAudioCommandAsync(
                RecordingAudioCommand.ToggleMuteAll,
                CancellationToken.None));

        gateway.CompleteFirstUpdate();
        await acceptedUpdate;
        await engine.WaitUntilStopRequestedAsync();
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "audio-stop.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "audio-stop.mp4"));
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 90,
            AudioPacketCount: 142));
        await finalizer.WaitUntilRequestedAsync();
        finalizer.Complete(new FinalizedRecording(pending.FinalPath));
        await stopping;

        Assert.Equal(RecorderState.Ready, coordinator.State);
        Assert.Null(coordinator.CurrentAudioControlState);
    }

    [Fact]
    public async Task CompetingStopReasonsShareOneSafeFinalizationAndRetainFirstReason()
    {
        var engine = new FakeRecordingEngine();
        var finalizer = new ControllableRecordingFileFinalizer();
        var savedRecordings = new FakeSavedRecordingSink();
        var finalization = new RecordingFileFinalizationUseCase(
            finalizer,
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            new FakeRecordingRecoveryStore(),
            savedRecordings);
        var coordinator = new ActiveRecordingSessionCoordinator(
            engine,
            finalization);
        var handle = new RecordingHandle(
            "session-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        coordinator.Activate(handle);

        var diskLow = coordinator.RequestStopAsync(
            new RecordingStopRequest(handle, RecordingStopReason.DiskLow),
            CancellationToken.None);
        var user = coordinator.RequestStopAsync(
            new RecordingStopRequest(handle, RecordingStopReason.UserRequested),
            CancellationToken.None);

        Assert.Equal(1, engine.StopCallCount);
        Assert.Equal(RecordingStopReason.DiskLow, coordinator.StopReason);
        Assert.Equal(RecorderState.Stopping, coordinator.State);
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "take.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "take.mp4"));
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 90,
            AudioPacketCount: 142));
        await finalizer.WaitUntilRequestedAsync();

        Assert.False(diskLow.IsCompleted);
        Assert.False(user.IsCompleted);
        Assert.Empty(savedRecordings.Recordings);
        Assert.Equal(RecorderState.Stopping, coordinator.State);

        var finalized = new FinalizedRecording(pending.FinalPath);
        finalizer.Complete(finalized);
        await Task.WhenAll(diskLow, user);

        Assert.Equal(finalized, Assert.Single(savedRecordings.Recordings));
        Assert.Equal(RecorderState.Ready, coordinator.State);
        Assert.Equal(1, engine.StopCallCount);
    }

    private static ActiveRecordingSessionCoordinator CreateCoordinator(
        IRecordingEngine engine,
        IRecordingAudioRoutingGateway audioRoutingGateway)
    {
        var finalization = new RecordingFileFinalizationUseCase(
            new ControllableRecordingFileFinalizer(),
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            new FakeRecordingRecoveryStore(),
            new FakeSavedRecordingSink());
        return new ActiveRecordingSessionCoordinator(
            engine,
            finalization,
            audioRoutingGateway);
    }

    private sealed class CapturingAudioRoutingGateway
        : IRecordingAudioRoutingGateway
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _firstUpdateStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstUpdate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<(RecordingHandle Handle, AudioRouting Routing)>
            _updates = [];

        public bool BlockFirstUpdate { get; init; }

        public List<(RecordingHandle Handle, AudioRouting Routing)> Updates
        {
            get
            {
                lock (_gate)
                {
                    return [.. _updates];
                }
            }
        }

        public async Task UpdateAudioRoutingAsync(
            RecordingHandle handle,
            AudioRouting routing,
            CancellationToken cancellationToken)
        {
            var isFirst = false;
            lock (_gate)
            {
                _updates.Add((handle, routing));
                isFirst = _updates.Count == 1;
            }

            if (isFirst)
            {
                _firstUpdateStarted.TrySetResult();
                if (BlockFirstUpdate)
                {
                    await _releaseFirstUpdate.Task
                        .WaitAsync(cancellationToken);
                }
            }
        }

        public Task WaitUntilFirstUpdateStartedAsync() =>
            _firstUpdateStarted.Task;

        public void CompleteFirstUpdate() => _releaseFirstUpdate.TrySetResult();
    }
}
