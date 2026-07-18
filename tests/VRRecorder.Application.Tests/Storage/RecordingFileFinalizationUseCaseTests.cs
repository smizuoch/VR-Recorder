using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;

namespace VRRecorder.Application.Tests.Storage;

public sealed class RecordingFileFinalizationUseCaseTests
{
    [Fact]
    public void ConstructorRejectsNullRequiredDependencies()
    {
        var finalizer = new UnexpectedRecordingFileFinalizer();
        var validator = new StubRecordingFileValidator(
            RecordingFileValidation.Valid);
        var recovery = new FakeRecordingRecoveryStore();
        var savedRecordings = new FakeSavedRecordingSink();

        var finalizerException = Assert.Throws<ArgumentNullException>(() =>
            new RecordingFileFinalizationUseCase(
                null!,
                validator,
                recovery,
                savedRecordings));
        Assert.Equal("finalizer", finalizerException.ParamName);

        var validatorException = Assert.Throws<ArgumentNullException>(() =>
            new RecordingFileFinalizationUseCase(
                finalizer,
                null!,
                recovery,
                savedRecordings));
        Assert.Equal("validator", validatorException.ParamName);

        var recoveryException = Assert.Throws<ArgumentNullException>(() =>
            new RecordingFileFinalizationUseCase(
                finalizer,
                validator,
                null!,
                savedRecordings));
        Assert.Equal("recovery", recoveryException.ParamName);

        var savedException = Assert.Throws<ArgumentNullException>(() =>
            new RecordingFileFinalizationUseCase(
                finalizer,
                validator,
                recovery,
                null!));
        Assert.Equal("savedRecordings", savedException.ParamName);
    }

    [Fact]
    public async Task ExecuteRejectsNullPendingAndStopResults()
    {
        var useCase = new RecordingFileFinalizationUseCase(
            new UnexpectedRecordingFileFinalizer(),
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            new FakeRecordingRecoveryStore(),
            new FakeSavedRecordingSink());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.ExecuteAsync(
                (PendingRecording)null!,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            useCase.ExecuteAsync(
                (RecordingStopResult)null!,
                CancellationToken.None));
    }

    [Fact]
    public async Task PendingIsValidatedBeforeFinalNameIsPublished()
    {
        var calls = new List<string>();
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "recording.mp4"));
        var useCase = new RecordingFileFinalizationUseCase(
            new OrderedRecordingFileFinalizer(calls),
            new OrderedRecordingFileValidator(calls),
            new FakeRecordingRecoveryStore(),
            new OrderedSavedRecordingSink(calls));

        var result = await useCase.ExecuteAsync(
            pending,
            CancellationToken.None);

        Assert.IsType<RecordingFinalizationResult.Saved>(result);
        Assert.Equal(
            [
                $"validate:{pending.TemporaryPath}",
                $"finalize:{pending.TemporaryPath}",
                $"saved:{pending.FinalPath}",
            ],
            calls);
    }

    [Fact]
    public async Task FinalizationFailureAfterValidationQuarantinesWithoutSaved()
    {
        var recovery = new FakeRecordingRecoveryStore();
        var savedRecordings = new FakeSavedRecordingSink();
        var useCase = new RecordingFileFinalizationUseCase(
            new FailingRecordingFileFinalizer(),
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
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
        var validator = new StubRecordingFileValidator(
            RecordingFileValidation.Invalid);
        var recovery = new FakeRecordingRecoveryStore();
        var savedRecordings = new FakeSavedRecordingSink();
        var diagnostics = new CapturingRecordingFinalizationEventSink();
        var useCase = new RecordingFileFinalizationUseCase(
            new UnexpectedRecordingFileFinalizer(),
            validator,
            recovery,
            savedRecordings,
            diagnostics);
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "recording.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "recording.mp4"));

        var execution = useCase.ExecuteAsync(pending, CancellationToken.None);

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
            new RecoverableRecording(pending.TemporaryPath),
            Assert.Single(recovery.Recordings));
        Assert.Empty(savedRecordings.Recordings);
        Assert.Equal(
            [RecordingRecoveryReason.ValidationFailed],
            diagnostics.Reasons);
    }

    [Fact]
    public async Task RecoveryDiagnosticFailureCannotChangeQuarantinedResult()
    {
        var recovery = new FakeRecordingRecoveryStore();
        var diagnostics = new ThrowingRecordingFinalizationEventSink();
        var useCase = new RecordingFileFinalizationUseCase(
            new FailingRecordingFileFinalizer(),
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            recovery,
            new FakeSavedRecordingSink(),
            diagnostics);
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
            [RecordingRecoveryReason.FinalizationFailed],
            diagnostics.Reasons);
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

    private sealed class UnexpectedRecordingFileFinalizer
        : IRecordingFileFinalizer
    {
        public Task<FinalizedRecording> FinalizeAsync(
            PendingRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Finalization was not expected.");
    }

    private sealed class OrderedRecordingFileFinalizer(List<string> calls)
        : IRecordingFileFinalizer
    {
        public Task<FinalizedRecording> FinalizeAsync(
            PendingRecording recording,
            CancellationToken cancellationToken)
        {
            calls.Add($"finalize:{recording.TemporaryPath}");
            return Task.FromResult(new FinalizedRecording(recording.FinalPath));
        }
    }

    private sealed class OrderedRecordingFileValidator(List<string> calls)
        : IRecordingFileValidator
    {
        public Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            calls.Add($"validate:{recording.FinalPath}");
            return Task.FromResult(RecordingFileValidation.Valid);
        }
    }

    private sealed class OrderedSavedRecordingSink(List<string> calls)
        : ISavedRecordingSink
    {
        public Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            calls.Add($"saved:{recording.FinalPath}");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRecordingFinalizationEventSink
        : IRecordingFinalizationEventSink
    {
        public List<RecordingRecoveryReason> Reasons { get; } = [];

        public void Publish(RecordingRecoveryReason reason)
        {
            Reasons.Add(reason);
            throw new IOException("diagnostic storage unavailable");
        }
    }

    private sealed class CapturingRecordingFinalizationEventSink
        : IRecordingFinalizationEventSink
    {
        public List<RecordingRecoveryReason> Reasons { get; } = [];

        public void Publish(RecordingRecoveryReason reason) =>
            Reasons.Add(reason);
    }
}
