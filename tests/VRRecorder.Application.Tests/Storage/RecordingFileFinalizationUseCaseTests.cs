using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;

namespace VRRecorder.Application.Tests.Storage;

public sealed class RecordingFileFinalizationUseCaseTests
{
    [Fact]
    public async Task SavedIsNotPublishedBeforeFinalRenameCompletes()
    {
        var finalizer = new ControllableRecordingFileFinalizer();
        var validator = new StubRecordingFileValidator(
            RecordingFileValidation.Valid);
        var recovery = new FakeRecordingRecoveryStore();
        var savedRecordings = new FakeSavedRecordingSink();
        var useCase = new RecordingFileFinalizationUseCase(
            finalizer,
            validator,
            recovery,
            savedRecordings);
        var pending = new PendingRecording(
            "recording.recording.mp4",
            "recording.mp4");

        var execution = useCase.ExecuteAsync(pending, CancellationToken.None);
        await finalizer.WaitUntilRequestedAsync();

        Assert.Empty(savedRecordings.Recordings);

        var finalized = new FinalizedRecording("recording.mp4");
        finalizer.Complete(finalized);
        var result = await execution;

        var saved = Assert.IsType<RecordingFinalizationResult.Saved>(result);
        Assert.Equal(finalized, saved.Recording);
        Assert.Equal(finalized, Assert.Single(savedRecordings.Recordings));
        Assert.Empty(recovery.Recordings);
    }

    [Fact]
    public async Task InvalidMp4IsQuarantinedWithoutSavedNotification()
    {
        var finalizer = new ControllableRecordingFileFinalizer();
        var validator = new StubRecordingFileValidator(
            RecordingFileValidation.Invalid);
        var recovery = new FakeRecordingRecoveryStore();
        var savedRecordings = new FakeSavedRecordingSink();
        var useCase = new RecordingFileFinalizationUseCase(
            finalizer,
            validator,
            recovery,
            savedRecordings);
        var pending = new PendingRecording(
            "recording.recording.mp4",
            "recording.mp4");

        var execution = useCase.ExecuteAsync(pending, CancellationToken.None);
        await finalizer.WaitUntilRequestedAsync();
        var finalized = new FinalizedRecording("recording.mp4");
        finalizer.Complete(finalized);

        var result = await execution;

        var recoveryRequired =
            Assert.IsType<RecordingFinalizationResult.RecoveryRequired>(result);
        Assert.Equal(finalized, recoveryRequired.Recording);
        Assert.Equal(finalized, Assert.Single(recovery.Recordings));
        Assert.Empty(savedRecordings.Recordings);
    }
}
