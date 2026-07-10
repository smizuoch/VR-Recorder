using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Recording;

public sealed class StartRecordingUseCaseTests
{
    private static readonly OutputPath TestOutputPath = new(Path.GetTempPath());
    private static readonly FrameRate TestFrameRate = new(30);
    private static readonly DateTimeOffset TestLocalNow = new(
        2026,
        7,
        10,
        12,
        34,
        56,
        TimeSpan.FromHours(9));

    [Fact]
    public async Task ExecuteDoesNotStartEngineBeforeStableSignal()
    {
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var engine = new FakeRecordingEngine();
        var useCase = CreateUseCase(signal, countdown, engine);
        using var cancellation = new CancellationTokenSource();

        var execution = useCase.ExecuteAsync(
            Command(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite),
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
        var useCase = CreateUseCase(signal, countdown, engine);

        var execution = useCase.ExecuteAsync(
            Command(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite),
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
        var useCase = CreateUseCase(signal, countdown, engine);
        using var cancellation = new CancellationTokenSource();

        var execution = useCase.ExecuteAsync(
            Command(
                SelfTimer.FromSeconds(3),
                RecordingDuration.Infinite),
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
    public async Task ReservesOutputAfterCountdownAndBeforeEngineStart()
    {
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var reservation = new FakeRecordingFileReservation();
        var engine = new FakeRecordingEngine();
        var useCase = new StartRecordingUseCase(
            signal,
            countdown,
            reservation,
            new FixedWallClock(TestLocalNow),
            SufficientStorage(),
            engine,
            CreateAutoStopScheduler());
        var pending = new PendingRecording(
            Path.Combine(TestOutputPath.FullPath, "take.recording.mp4"),
            Path.Combine(TestOutputPath.FullPath, "take.mp4"));

        var execution = useCase.ExecuteAsync(
            Command(
                SelfTimer.FromSeconds(3),
                RecordingDuration.Infinite),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithStableSignal(new StableVideoSignal(1920, 1080));
        await countdown.WaitUntilRequestedAsync();

        Assert.Null(reservation.RequestedDescriptor);
        countdown.Complete();
        await reservation.WaitUntilRequestedAsync();

        Assert.Equal(0, engine.StartCallCount);
        Assert.Equal(TestOutputPath, reservation.RequestedOutputPath);
        Assert.Equal(1920, reservation.RequestedDescriptor?.Width);
        Assert.Equal(1080, reservation.RequestedDescriptor?.Height);
        Assert.Equal(TestFrameRate, reservation.RequestedDescriptor?.FrameRate);
        Assert.Equal(
            TestLocalNow,
            reservation.RequestedDescriptor?.Timestamp.LocalStartedAt);

        reservation.Complete(pending);
        await engine.WaitUntilStartRequestedAsync();
        Assert.Equal(pending, Assert.Single(engine.StartedPlans).Output);
        engine.CommitFirstPacket(new RecordingHandle(
            "session-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero)));

        Assert.IsType<StartRecordingResult.Started>(await execution);
    }

    [Fact]
    public async Task InsufficientStorageDoesNotReserveOrStartMedia()
    {
        var signal = new ControllableVideoSignalGateway();
        var countdown = new ControllableCountdownTimer();
        var reservation = new FakeRecordingFileReservation();
        var storage = new StubStorageSpaceProbe(new StorageSpace(
            StorageCapacityPolicy.MinimumStartBytes - 1));
        var engine = new FakeRecordingEngine();
        var useCase = new StartRecordingUseCase(
            signal,
            countdown,
            reservation,
            new FixedWallClock(TestLocalNow),
            storage,
            engine,
            CreateAutoStopScheduler());

        var execution = useCase.ExecuteAsync(
            Command(
                SelfTimer.FromSeconds(3),
                RecordingDuration.Infinite),
            CancellationToken.None);
        await signal.WaitUntilRequestedAsync();
        signal.CompleteWithStableSignal(new StableVideoSignal(1920, 1080));
        await countdown.WaitUntilRequestedAsync();
        countdown.Complete();

        var result = Assert.IsType<StartRecordingResult.InsufficientStorage>(
            await execution);

        Assert.Equal(
            StorageCapacityPolicy.MinimumStartBytes - 1,
            result.AvailableSpace.AvailableBytes);
        Assert.Equal(TestOutputPath, storage.RequestedOutputPath);
        Assert.Null(reservation.RequestedDescriptor);
        Assert.Equal(0, engine.StartCallCount);
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
            CompletedReservation(),
            new FixedWallClock(TestLocalNow),
            SufficientStorage(),
            engine,
            autoStop);

        var execution = useCase.ExecuteAsync(
            Command(
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

    private static StartRecordingUseCase CreateUseCase(
        ControllableVideoSignalGateway signal,
        ControllableCountdownTimer countdown,
        FakeRecordingEngine engine)
    {
        var clock = new ControllableMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        return new StartRecordingUseCase(
            signal,
            countdown,
            CompletedReservation(),
            new FixedWallClock(TestLocalNow),
            SufficientStorage(),
            engine,
            new AutoStopScheduler(clock, new FakeStopRequestSink()));
    }

    private static StartRecordingCommand Command(
        SelfTimer selfTimer,
        RecordingDuration autoStop) =>
        new(selfTimer, autoStop, TestOutputPath, TestFrameRate);

    private static FakeRecordingFileReservation CompletedReservation()
    {
        var reservation = new FakeRecordingFileReservation();
        reservation.Complete(new PendingRecording(
            Path.Combine(TestOutputPath.FullPath, "take.recording.mp4"),
            Path.Combine(TestOutputPath.FullPath, "take.mp4")));
        return reservation;
    }

    private static AutoStopScheduler CreateAutoStopScheduler() =>
        new(
            new ControllableMonotonicClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new FakeStopRequestSink());

    private static StubStorageSpaceProbe SufficientStorage() =>
        new(new StorageSpace(StorageCapacityPolicy.MinimumStartBytes));
}
