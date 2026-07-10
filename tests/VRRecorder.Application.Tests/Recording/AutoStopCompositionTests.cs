using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class AutoStopCompositionTests
{
    [Fact]
    public async Task AutoStopAndCompetingUserStopShareOneSafeFinalization()
    {
        var firstPacketAt = MonotonicTimestamp.FromElapsed(
            TimeSpan.FromSeconds(100));
        var handle = new RecordingHandle("session-001", firstPacketAt);
        var pending = new PendingRecording(
            Path.Combine(Path.GetTempPath(), "auto-stop.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "auto-stop.mp4"));
        var engine = new FakeRecordingEngine();
        engine.CompleteStop(new RecordingStopResult(
            pending,
            VideoPacketCount: 90,
            AudioPacketCount: 142));
        var finalizer = new ControllableRecordingFileFinalizer();
        var finalized = new FinalizedRecording(pending.FinalPath);
        finalizer.Complete(finalized);
        var savedRecordings = new CompetingUserStopSavedRecordingSink();
        var finalization = new RecordingFileFinalizationUseCase(
            finalizer,
            new StubRecordingFileValidator(RecordingFileValidation.Valid),
            new FakeRecordingRecoveryStore(),
            savedRecordings);
        var lifecycle = new ActiveRecordingSessionCoordinator(
            engine,
            finalization);
        savedRecordings.Configure(lifecycle, handle);
        lifecycle.Activate(handle);
        var clock = new ControllableMonotonicClock(firstPacketAt);
        var scheduler = new AutoStopScheduler(clock, lifecycle);

        var autoStop = scheduler.OnFirstPacketCommittedAsync(
            handle,
            RecordingDuration.FromSeconds(3),
            CancellationToken.None);
        var deadline = await clock.WaitUntilDeadlineRequestedAsync();

        Assert.Equal(firstPacketAt.Add(TimeSpan.FromSeconds(3)), deadline);
        Assert.Equal(0, engine.StopCallCount);

        clock.AdvanceBy(TimeSpan.FromSeconds(3));
        await autoStop;
        Assert.NotNull(savedRecordings.CompetingStop);
        await savedRecordings.CompetingStop;

        Assert.False(savedRecordings.CompetingStopCompletedSynchronously);
        Assert.Equal(1, engine.StopCallCount);
        Assert.Equal(finalized, Assert.Single(savedRecordings.Recordings));
        Assert.Equal(RecordingStopReason.AutoStop, lifecycle.StopReason);
        Assert.Equal(RecorderState.Ready, lifecycle.State);
    }

    private sealed class CompetingUserStopSavedRecordingSink
        : ISavedRecordingSink
    {
        private ActiveRecordingSessionCoordinator? _lifecycle;
        private RecordingHandle? _handle;
        private bool _competingStopRequested;

        public List<FinalizedRecording> Recordings { get; } = [];

        public Task? CompetingStop { get; private set; }

        public bool CompetingStopCompletedSynchronously { get; private set; }

        public void Configure(
            ActiveRecordingSessionCoordinator lifecycle,
            RecordingHandle handle)
        {
            _lifecycle = lifecycle;
            _handle = handle;
        }

        public Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recordings.Add(recording);
            if (!_competingStopRequested)
            {
                _competingStopRequested = true;
                CompetingStop = _lifecycle!.RequestStopAsync(
                    new RecordingStopRequest(
                        _handle!,
                        RecordingStopReason.UserRequested),
                    CancellationToken.None);
                CompetingStopCompletedSynchronously = CompetingStop.IsCompleted;
            }

            return Task.CompletedTask;
        }
    }
}
