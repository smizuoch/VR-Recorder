using VRRecorder.Application.Recording;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Tests.Recording;

public sealed class StartRecordingUseCaseTests
{
    [Fact]
    public async Task ExecuteDoesNotStartEngineBeforeStableSignal()
    {
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var engine = new FakeRecordingEngine();
        var useCase = new StartRecordingUseCase(signal, countdown, engine);
        using var cancellation = new CancellationTokenSource();

        var execution = useCase.ExecuteAsync(
            new StartRecordingCommand(SelfTimer.FromSeconds(0)),
            cancellation.Token);
        await signal.WaitUntilRequestedAsync();

        Assert.Equal(0, engine.StartCallCount);
        Assert.Empty(engine.CreatedFiles);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    [Fact]
    public async Task SignalTimeoutReturnsNoSignalWithoutCreatingAFile()
    {
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var engine = new FakeRecordingEngine();
        var useCase = new StartRecordingUseCase(signal, countdown, engine);

        var execution = useCase.ExecuteAsync(
            new StartRecordingCommand(SelfTimer.FromSeconds(0)),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithTimeout();

        var result = await execution;

        Assert.IsType<StartRecordingResult.NoSignal>(result);
        Assert.Equal(0, engine.StartCallCount);
        Assert.Empty(engine.CreatedFiles);
    }

    [Fact]
    public async Task CancelDuringCountdownDoesNotStartEngine()
    {
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var engine = new FakeRecordingEngine();
        var useCase = new StartRecordingUseCase(signal, countdown, engine);
        using var cancellation = new CancellationTokenSource();

        var execution = useCase.ExecuteAsync(
            new StartRecordingCommand(SelfTimer.FromSeconds(3)),
            cancellation.Token);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithStableSignal(new StableVideoSignal(1920, 1080));
        await countdown.WaitUntilRequestedAsync();

        Assert.Equal(0, engine.StartCallCount);
        Assert.Empty(engine.CreatedFiles);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(0, engine.StartCallCount);
        Assert.Empty(engine.CreatedFiles);
    }

    [Fact]
    public async Task AutoStopDeadlineStartsAtEngineFirstPacketCommit()
    {
        var initialNow = MonotonicTimestamp.FromElapsed(TimeSpan.Zero);
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var engine = new FakeRecordingEngine();
        var clock = new ControllableMonotonicClock(initialNow);
        var stopRequests = new FakeStopRequestSink();
        var autoStop = new AutoStopScheduler(clock, stopRequests);
        var useCase = new StartRecordingUseCase(
            signal,
            countdown,
            engine,
            autoStop);

        var execution = useCase.ExecuteAsync(
            new StartRecordingCommand(
                SelfTimer.FromSeconds(3),
                RecordingDuration.FromSeconds(3)),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();

        clock.AdvanceBy(TimeSpan.FromSeconds(10));
        Assert.Equal(0, clock.DelayCallCount);

        signal.CompleteWithStableSignal(new StableVideoSignal(1920, 1080));
        await countdown.WaitUntilRequestedAsync();
        clock.AdvanceBy(TimeSpan.FromSeconds(10));
        Assert.Equal(0, clock.DelayCallCount);

        countdown.Complete();
        await engine.WaitUntilStartRequestedAsync();
        clock.AdvanceBy(TimeSpan.FromSeconds(10));
        Assert.Equal(0, clock.DelayCallCount);

        var committedAt = clock.Now;
        engine.CommitFirstPacket(new RecordingHandle("session-001", committedAt));
        var started = Assert.IsType<StartRecordingResult.Started>(await execution);
        var deadline = await clock.WaitUntilDeadlineRequestedAsync();

        Assert.Equal(committedAt.Add(TimeSpan.FromSeconds(3)), deadline);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(2999));
        Assert.Empty(stopRequests.RequestedHandles);

        clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await started.AutoStopCompletion;
        Assert.Single(stopRequests.RequestedHandles);
    }
}
