using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingSessionStopCoordinatorTests
{
    [Fact]
    public async Task DuplicateStopsShareStopFinalizeValidateAndSavedPipeline()
    {
        var engine = new FakeRecordingEngine();
        var finalizer = new ControllableRecordingFileFinalizer();
        var savedRecordings = new FakeSavedRecordingSink();
        var finalization = new RecordingFileFinalizationUseCase(
            finalizer,
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            new FakeRecordingRecoveryStore(),
            savedRecordings);
        var handle = new RecordingHandle(
            "session-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var coordinator = new RecordingSessionStopCoordinator(
            engine,
            handle,
            finalization);

        var first = coordinator.StopAsync();
        var second = coordinator.StopAsync();

        Assert.Same(first, second);
        Assert.Equal(1, engine.StopCallCount);
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "recording.mp4"));
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 90,
            AudioPacketCount: 142));
        await finalizer.WaitUntilRequestedAsync();
        Assert.False(first.IsCompleted);
        Assert.Empty(savedRecordings.Recordings);

        var finalized = new FinalizedRecording("recording.mp4");
        finalizer.Complete(finalized);
        var result = await first;

        Assert.IsType<RecordingFinalizationResult.Saved>(result);
        Assert.Equal(finalized, Assert.Single(savedRecordings.Recordings));
        Assert.Equal(result, await second);
        Assert.Same(first, coordinator.StopAsync());
        Assert.Equal(1, engine.StopCallCount);
    }
}
