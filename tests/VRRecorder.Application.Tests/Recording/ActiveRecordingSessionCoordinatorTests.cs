using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class ActiveRecordingSessionCoordinatorTests
{
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
}
