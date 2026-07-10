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
    public async Task DuplicateSessionIdAbortsNewlyOpenedSession()
    {
        var backend = new DuplicateIdNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var engine = new NativeRecordingEngine(
            backend,
            clock,
            new CapturingRuntimeFaultSink());
        var firstStart = engine.StartAsync(CreatePlan(), CancellationToken.None);
        backend.SignalFirstVideoPacketMuxed(sessionIndex: 0);
        await firstStart;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.StartAsync(CreatePlan(), CancellationToken.None));

        Assert.Equal(
            "Native recording session native-session-001 already exists.",
            exception.Message);
        Assert.Equal(0, backend.Sessions[0].AbortCallCount);
        Assert.Equal(1, backend.Sessions[1].AbortCallCount);
    }

    [Fact]
    public async Task CancelledStopLeavesNativeSessionAvailableForRetry()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var engine = new NativeRecordingEngine(
            backend,
            clock,
            new CapturingRuntimeFaultSink());
        var start = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await start;
        using var cancellation = new CancellationTokenSource();

        var cancelledStop = engine.StopAsync(handle, cancellation.Token);
        await backend.Session.WaitUntilFirstStopStartedAsync();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledStop);
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(CreatePlan().Output, stopped.Recording);
        Assert.Equal(90, stopped.VideoPacketCount);
        Assert.Equal(142, stopped.AudioPacketCount);
        Assert.Equal(2, backend.Session.StopCallCount);
    }

    [Fact]
    public async Task FailedNativeStopAbortsAndRemovesTerminalSession()
    {
        var backend = new ControllableNativeRecordingBackend();
        var stopFailure = new IOException("encoder stopped unexpectedly");
        backend.Session.StopFailure = stopFailure;
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink());
        var starting = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await starting;

        var observed = await Assert.ThrowsAsync<IOException>(() =>
            engine.StopAsync(handle, CancellationToken.None));

        Assert.Same(stopFailure, observed);
        Assert.Equal(1, backend.Session.StopCallCount);
        Assert.Equal(1, backend.Session.AbortCallCount);
        var inactive = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.StopAsync(handle, CancellationToken.None));
        Assert.Equal(
            "Native recording session native-session-001 is not active.",
            inactive.Message);
    }

    [Fact]
    public async Task AbortCleanupFailureDoesNotReplaceNativeStopFailure()
    {
        var backend = new ControllableNativeRecordingBackend();
        var stopFailure = new IOException("muxer stop failed");
        backend.Session.StopFailure = stopFailure;
        backend.Session.AbortFailure = new InvalidOperationException(
            "native abort failed");
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink());
        var starting = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await starting;

        var observed = await Assert.ThrowsAsync<IOException>(() =>
            engine.StopAsync(handle, CancellationToken.None));

        Assert.Same(stopFailure, observed);
        Assert.Equal(1, backend.Session.AbortCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.StopAsync(handle, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationBeforeFirstPacketAbortsOpenedNativeSession()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var engine = new NativeRecordingEngine(
            backend,
            clock,
            new CapturingRuntimeFaultSink());
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
        var engine = new NativeRecordingEngine(
            backend,
            clock,
            new CapturingRuntimeFaultSink());

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

    [Fact]
    public async Task FaultBeforeFirstPacketFailsStartAndAbortsSession()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var engine = new NativeRecordingEngine(
            backend,
            clock,
            new CapturingRuntimeFaultSink());

        var start = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFault(new NativeRecordingFault(
            Status: 6,
            Message: "encoder initialization failed"));

        var exception = await Assert.ThrowsAsync<NativeRecordingException>(
            () => start);

        Assert.Equal(6, exception.Fault.Status);
        Assert.Equal("encoder initialization failed", exception.Fault.Message);
        Assert.Equal(1, backend.Session.AbortCallCount);
    }

    [Fact]
    public async Task FaultAfterFirstPacketIsReportedToRuntimeFaultSink()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero));
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var engine = new NativeRecordingEngine(backend, clock, runtimeFaults);
        var start = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await start;
        var fault = new NativeRecordingFault(
            Status: 6,
            Message: "encoder failed while recording");

        backend.SignalFault(fault);

        var report = Assert.Single(runtimeFaults.Reports);
        Assert.Equal(handle, report.Handle);
        Assert.Equal(fault, report.Fault);
    }

    [Fact]
    public async Task ImmediatePostPacketFaultWaitsForActivatedRecordingHandle()
    {
        var backend = new ControllableNativeRecordingBackend();
        var clock = new ControllableClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(4)));
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var engine = new NativeRecordingEngine(backend, clock, runtimeFaults);
        var starting = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        var fault = new NativeRecordingFault(6, "immediate encoder failure");

        backend.SignalFirstVideoPacketMuxed();
        backend.SignalFault(fault);
        var handle = await starting;

        var report = Assert.Single(runtimeFaults.Reports);
        Assert.Equal(handle, report.Handle);
        Assert.Equal(fault, report.Fault);
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
        private NativeRecordingCallbacks? _callbacks;

        public StubNativeRecordingSession Session { get; } = new();

        public Task<INativeRecordingSession> OpenAsync(
            RecordingPlan plan,
            NativeRecordingCallbacks callbacks,
            CancellationToken cancellationToken)
        {
            _callbacks = callbacks;
            _opened.TrySetResult();
            return Task.FromResult<INativeRecordingSession>(Session);
        }

        public void SignalFirstVideoPacketMuxed() =>
            (_callbacks ??
             throw new InvalidOperationException("The backend is not open."))
            .FirstVideoPacketMuxed();

        public void SignalFault(NativeRecordingFault fault) =>
            (_callbacks ??
             throw new InvalidOperationException("The backend is not open."))
            .Faulted(fault);

        public Task WaitUntilOpenedAsync() => _opened.Task;
    }

    private sealed class DuplicateIdNativeRecordingBackend
        : INativeRecordingBackend
    {
        private readonly List<NativeRecordingCallbacks> _callbacks = [];
        private int _nextSession;

        public IReadOnlyList<StubNativeRecordingSession> Sessions { get; } =
        [
            new(),
            new(),
        ];

        public Task<INativeRecordingSession> OpenAsync(
            RecordingPlan plan,
            NativeRecordingCallbacks callbacks,
            CancellationToken cancellationToken)
        {
            _callbacks.Add(callbacks);
            return Task.FromResult<INativeRecordingSession>(
                Sessions[_nextSession++]);
        }

        public void SignalFirstVideoPacketMuxed(int sessionIndex) =>
            _callbacks[sessionIndex].FirstVideoPacketMuxed();
    }

    private sealed class StubNativeRecordingSession : INativeRecordingSession
    {
        private readonly TaskCompletionSource _firstStopStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int AbortCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Exception? AbortFailure { get; set; }

        public Exception? StopFailure { get; set; }

        public string Id => "native-session-001";

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            AbortCallCount++;
            return AbortFailure is null
                ? Task.CompletedTask
                : Task.FromException(AbortFailure);
        }

        public async Task<RecordingStopResult> StopAsync(
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            if (StopFailure is not null)
            {
                throw StopFailure;
            }

            if (StopCallCount == 1)
            {
                _firstStopStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new RecordingStopResult(
                new PendingRecording(
                    Path.Combine(Path.GetTempPath(), "take.recording.mp4"),
                    Path.Combine(Path.GetTempPath(), "take.mp4")),
                VideoPacketCount: 90,
                AudioPacketCount: 142);
        }

        public Task WaitUntilFirstStopStartedAsync() => _firstStopStarted.Task;
    }

    private sealed class CapturingRuntimeFaultSink
        : INativeRecordingRuntimeFaultSink
    {
        public List<(RecordingHandle Handle, NativeRecordingFault Fault)>
            Reports
        { get; } = [];

        public void Report(
            RecordingHandle handle,
            NativeRecordingFault fault) => Reports.Add((handle, fault));
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
