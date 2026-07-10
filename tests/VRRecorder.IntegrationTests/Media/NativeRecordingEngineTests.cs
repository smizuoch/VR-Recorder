using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class NativeRecordingEngineTests
{
    [Fact]
    public async Task StartCompletesOnlyAfterNativeFirstPacketCallback()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(10)));
        var engine = new NativeRecordingEngine(backend, clock);

        var start = engine.StartAsync(
            new RecordingPlan(new StableVideoSignal(320, 180)),
            CancellationToken.None);
        await backend.WaitUntilOpenedAsync();

        Assert.False(start.IsCompleted);
        clock.Now = MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(12));
        backend.SignalFirstVideoPacketMuxed();
        var handle = await start;

        Assert.Equal("native-session-001", handle.Id);
        Assert.Equal(clock.Now, handle.FirstPacketCommittedAt);
    }

    private sealed class ControllableNativeRecordingBackend
        : INativeRecordingBackend
    {
        private readonly TaskCompletionSource _opened = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private Action? _firstVideoPacketMuxed;

        public Task<INativeRecordingSession> OpenAsync(
            RecordingPlan plan,
            Action firstVideoPacketMuxed,
            CancellationToken cancellationToken)
        {
            _firstVideoPacketMuxed = firstVideoPacketMuxed;
            _opened.TrySetResult();
            return Task.FromResult<INativeRecordingSession>(
                new StubNativeRecordingSession());
        }

        public void SignalFirstVideoPacketMuxed() =>
            (_firstVideoPacketMuxed ??
             throw new InvalidOperationException("The backend is not open."))();

        public Task WaitUntilOpenedAsync() => _opened.Task;
    }

    private sealed class StubNativeRecordingSession : INativeRecordingSession
    {
        public string Id => "native-session-001";

        public Task<RecordingResult> StopAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new RecordingResult("take.recording.mp4"));
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
