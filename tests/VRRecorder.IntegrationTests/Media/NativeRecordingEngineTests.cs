using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class NativeRecordingEngineTests
{
    [Fact]
    public async Task CancellationBeforeFirstPacketAbortsOpenedNativeSession()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var engine = new NativeRecordingEngine(backend, clock);
        using var cancellation = new CancellationTokenSource();

        var start = engine.StartAsync(
            CreatePlan(),
            cancellation.Token);
        await backend.WaitUntilOpenedAsync();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(1, backend.Session.AbortCallCount);
    }

    [Fact]
    public async Task StartCompletesOnlyAfterNativeFirstPacketCallback()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(10)));
        var engine = new NativeRecordingEngine(backend, clock);

        var start = engine.StartAsync(
            CreatePlan(),
            CancellationToken.None);
        await backend.WaitUntilOpenedAsync();

        Assert.False(start.IsCompleted);
        clock.Now = MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(12));
        backend.SignalFirstVideoPacketMuxed();
        var handle = await start;

        Assert.Equal("native-session-001", handle.Id);
        Assert.Equal(clock.Now, handle.FirstPacketCommittedAt);
    }

    private static RecordingPlan CreatePlan() =>
        new(
            new StableVideoSignal(320, 180),
            new PendingRecording(
                Path.Combine(Path.GetTempPath(), "take.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "take.mp4")),
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(30));

    private sealed class ControllableNativeRecordingBackend
        : INativeRecordingBackend
    {
        private readonly TaskCompletionSource _opened = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private Action? _firstVideoPacketMuxed;

        public StubNativeRecordingSession Session { get; } = new();

        public Task<INativeRecordingSession> OpenAsync(
            RecordingPlan plan,
            Action firstVideoPacketMuxed,
            CancellationToken cancellationToken)
        {
            _firstVideoPacketMuxed = firstVideoPacketMuxed;
            _opened.TrySetResult();
            return Task.FromResult<INativeRecordingSession>(Session);
        }

        public void SignalFirstVideoPacketMuxed() =>
            (_firstVideoPacketMuxed ??
             throw new InvalidOperationException("The backend is not open."))();

        public Task WaitUntilOpenedAsync() => _opened.Task;
    }

    private sealed class StubNativeRecordingSession : INativeRecordingSession
    {
        public int AbortCallCount { get; private set; }

        public string Id => "native-session-001";

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            AbortCallCount++;
            return Task.CompletedTask;
        }

        public Task<RecordingStopResult> StopAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new RecordingStopResult(
                new PendingRecording(
                    "take.recording.mp4",
                    "take.mp4"),
                VideoPacketCount: 90,
                AudioPacketCount: 142));
    }

    private sealed class ControllableClock : IMonotonicClock
    {
        public ControllableClock(MonotonicTimestamp now)
        {
            Now = now;
        }

        public MonotonicTimestamp Now { get; set; }

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Delay was not expected.");
    }
}
