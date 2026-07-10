using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;

namespace VRRecorder.Application.Tests.Storage;

public sealed class RecordingFileFinalizationUseCaseTests
{
    [Fact]
    public async Task FinalizationFailureQuarantinesPendingWithoutValidationOrSaved()
    {
        var recovery = new FakeRecordingRecoveryStore();
        var savedRecordings = new FakeSavedRecordingSink();
        var useCase = new RecordingFileFinalizationUseCase(
            new FailingRecordingFileFinalizer(),
            new UnexpectedRecordingFileValidator(),
            recovery,
            savedRecordings);
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "recording.mp4"));

        var result = await useCase.ExecuteAsync(
            pending,
            CancellationToken.None);

        var recoveryRequired =
            Assert.IsType<RecordingFinalizationResult.RecoveryRequired>(result);
        Assert.Equal(
            RecordingRecoveryReason.FinalizationFailed,
            recoveryRequired.Reason);
        Assert.Equal(
            recovery.QuarantinedRecording,
            recoveryRequired.Quarantine);
        Assert.Equal(
            new RecoverableRecording(pending.TemporaryPath),
            Assert.Single(recovery.Recordings));
        Assert.Empty(savedRecordings.Recordings);
    }

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
            Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "recording.mp4"));

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
            Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "recording.mp4"));

        var execution = useCase.ExecuteAsync(pending, CancellationToken.None);
        await finalizer.WaitUntilRequestedAsync();
        var finalized = new FinalizedRecording("recording.mp4");
        finalizer.Complete(finalized);

        var result = await execution;

        var recoveryRequired =
            Assert.IsType<RecordingFinalizationResult.RecoveryRequired>(result);
        Assert.Equal(
            RecordingRecoveryReason.ValidationFailed,
            recoveryRequired.Reason);
        Assert.Equal(
            recovery.QuarantinedRecording,
            recoveryRequired.Quarantine);
        Assert.Equal(
            new RecoverableRecording(finalized.FinalPath),
            Assert.Single(recovery.Recordings));
        Assert.Empty(savedRecordings.Recordings);
    }

    private sealed class FailingRecordingFileFinalizer : IRecordingFileFinalizer
    {
        public Task<FinalizedRecording> FinalizeAsync(
            PendingRecording recording,
            CancellationToken cancellationToken) =>
            Task.FromException<FinalizedRecording>(
                new RecordingFileFinalizationException(
                    "The recording could not be finalized.",
                    new RecoverableRecording(recording.TemporaryPath),
                    new IOException("Synthetic rename failure.")));
    }

    private sealed class UnexpectedRecordingFileValidator
        : IRecordingFileValidator
    {
        public Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Validation was not expected.");
    }
}
