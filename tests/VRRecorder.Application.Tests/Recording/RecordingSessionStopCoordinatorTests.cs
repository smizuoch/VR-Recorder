using VRRecorder.Application.Ports;
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

    [Fact]
    public async Task CanceledSessionLifetimeCannotInterruptSafeFinalization()
    {
        using var sessionLifetime = new CancellationTokenSource();
        var engine = new FakeRecordingEngine();
        var finalizer = new ControllableRecordingFileFinalizer();
        var finalization = new RecordingFileFinalizationUseCase(
            finalizer,
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            new FakeRecordingRecoveryStore(),
            new FakeSavedRecordingSink());
        var handle = new RecordingHandle(
            "session-shutdown",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var coordinator = new RecordingSessionStopCoordinator(
            engine,
            handle,
            finalization,
            sessionLifetime.Token);
        await sessionLifetime.CancelAsync();

        var stopping = coordinator.StopAsync();

        Assert.False(Assert.Single(engine.StopCancellationTokens).CanBeCanceled);
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "shutdown.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "shutdown.mp4"));
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 1,
            AudioPacketCount: 1));
        await finalizer.WaitUntilRequestedAsync();
        Assert.False(finalizer.RequestedCancellationToken.CanBeCanceled);

        finalizer.Complete(new FinalizedRecording(pending.FinalPath));
        Assert.IsType<RecordingFinalizationResult.Saved>(await stopping);
    }

    [Fact]
    public async Task StopResultExpectationIsUsedForSessionValidation()
    {
        var engine = new FakeRecordingEngine();
        var finalizer = new ControllableRecordingFileFinalizer();
        var validator = new CapturingSessionExpectationValidator();
        var finalization = new RecordingFileFinalizationUseCase(
            finalizer,
            validator,
            new FakeRecordingRecoveryStore(),
            new FakeSavedRecordingSink());
        var handle = new RecordingHandle(
            "session-dynamic-validation",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var coordinator = new RecordingSessionStopCoordinator(
            engine,
            handle,
            finalization);
        var expectation = new RecordingMediaExpectation(
            Width: 1918,
            Height: 1080,
            FramesPerSecond: 59,
            AudioSampleRate: 48000,
            AudioChannels: 2,
            ExpectedDuration: TimeSpan.FromSeconds(2));
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "dynamic.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "dynamic.mp4"));

        var stopping = coordinator.StopAsync();
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 118,
            AudioPacketCount: 96,
            MediaExpectation: expectation));
        await finalizer.WaitUntilRequestedAsync();
        var finalized = new FinalizedRecording(pending.FinalPath);
        finalizer.Complete(finalized);
        await stopping;

        Assert.Equal(expectation, validator.Expectation);
        Assert.Equal(
            new FinalizedRecording(pending.TemporaryPath),
            validator.Recording);
        Assert.False(validator.LegacyOverloadCalled);
    }

    private sealed class CapturingSessionExpectationValidator
        : IRecordingFileValidator
    {
        public RecordingMediaExpectation? Expectation { get; private set; }

        public FinalizedRecording? Recording { get; private set; }

        public bool LegacyOverloadCalled { get; private set; }

        public Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            LegacyOverloadCalled = true;
            return Task.FromResult(RecordingFileValidation.Valid);
        }

        public Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            RecordingMediaExpectation expectation,
            CancellationToken cancellationToken)
        {
            Recording = recording;
            Expectation = expectation;
            return Task.FromResult(RecordingFileValidation.Valid);
        }
    }
}
