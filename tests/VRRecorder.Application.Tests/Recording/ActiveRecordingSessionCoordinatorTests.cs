using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;
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

        var updated = await coordinator.ExecuteAudioCommandAsync(
            RecordingAudioCommand.ToggleMicrophone,
            CancellationToken.None);

        Assert.Equal(
            [(handle, AudioRouting.Mixed)],
            gateway.Updates);
        Assert.Equal(AudioRouting.Mixed, updated.EffectiveRouting);
        Assert.Equal(updated, coordinator.CurrentAudioControlState);
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
        public List<(RecordingHandle Handle, AudioRouting Routing)> Updates
        { get; } = [];

        public Task UpdateAudioRoutingAsync(
            RecordingHandle handle,
            AudioRouting routing,
            CancellationToken cancellationToken)
        {
            Updates.Add((handle, routing));
            return Task.CompletedTask;
        }
    }
}
