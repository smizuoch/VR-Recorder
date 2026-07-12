using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class NativeRecordingEngineTests
{
    [Fact]
    public async Task RoutesAudioUpdatesOnlyToAnActiveNativeSession()
    {
        var backend = new ControllableNativeRecordingBackend();
        backend.Session.BlockFirstStop = false;
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink());
        var starting = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await starting;

        await engine.UpdateAudioRoutingAsync(
            handle,
            AudioRouting.DesktopOnly,
            CancellationToken.None);

        Assert.Equal(
            [AudioRouting.DesktopOnly],
            backend.Session.AudioRoutingUpdates);
        await engine.StopAsync(handle, CancellationToken.None);
        var inactive = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.UpdateAudioRoutingAsync(
                handle,
                AudioRouting.Mixed,
                CancellationToken.None));
        Assert.Equal(
            "Native recording session native-session-001 is not active.",
            inactive.Message);
    }

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

    [Fact]
    public async Task AudioObserversCannotInterruptAnActiveNativeSession()
    {
        var backend = new ControllableNativeRecordingBackend();
        backend.Session.BlockFirstStop = false;
        var audioEvents = new ThrowingAudioSessionEventSink();
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink(),
            audioEvents);
        var starting = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await starting;
        var warning = new AudioSessionWarning(
            AudioSessionWarningKind.InputUnavailable,
            Domain.Audio.AudioInput.Microphone,
            FramePosition: 4_800);
        var recovered = new AudioSessionStatus(
            AudioSessionStatusKind.InputRecovered,
            Domain.Audio.AudioInput.Microphone,
            FramePosition: 9_600);

        var warningFailure = Record.Exception(() =>
            backend.SignalAudioWarning(warning));
        var statusFailure = Record.Exception(() =>
            backend.SignalAudioStatus(recovered));
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Null(warningFailure);
        Assert.Null(statusFailure);
        Assert.Equal([warning], audioEvents.Warnings);
        Assert.Equal([recovered], audioEvents.Statuses);
        Assert.Equal(CreatePlan().Output, stopped.Recording);
    }

    [Fact]
    public async Task PublishesCommittedMediaProfileAndFinalStatistics()
    {
        var backend = new ControllableNativeRecordingBackend();
        backend.Session.BlockFirstStop = false;
        var expectedStatistics = new RecordingSessionStatistics(
            SourceVideoFrameCount: 120,
            MuxedVideoPacketCount: 90,
            MuxedAudioPacketCount: 142,
            DroppedSourceVideoFrameCount: 30,
            DuplicatedOutputVideoFrameCount: 4,
            LatestEncodeLatency: TimeSpan.FromMicroseconds(2_400),
            MaximumEncodeLatency: TimeSpan.FromMicroseconds(8_000),
            AudioVideoOffset: TimeSpan.FromMicroseconds(-15_000));
        backend.Session.Statistics = expectedStatistics;
        var mediaEvents = new CapturingRecordingMediaEventSink();
        var environment = new RecordingEnvironmentSnapshot(
            "0.3.0",
            "10.0.26100",
            RecordingProcessArchitecture.X64,
            "ven_10de&dev_2684",
            GpuVendor.Nvidia,
            "32.0.15.6094");
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink(),
            new ThrowingAudioSessionEventSink(),
            mediaEvents,
            new StubRecordingEnvironmentSource(environment));
        var plan = CreatePlan();

        var starting = engine.StartAsync(plan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await starting;
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        var profile = Assert.Single(mediaEvents.Profiles);
        Assert.Equal(plan.Signal.Width, profile.SourceWidth);
        Assert.Equal(plan.Signal.Height, profile.SourceHeight);
        Assert.Equal(plan.Signal.PixelFormat, profile.SourcePixelFormat);
        Assert.Equal(
            plan.Signal.EstimatedSourceFramesPerSecond,
            profile.EstimatedSourceFramesPerSecond);
        Assert.Equal(
            plan.VideoLayout.CurrentLayout.OutputCanvas.Width,
            profile.OutputWidth);
        Assert.Equal(
            plan.VideoLayout.CurrentLayout.OutputCanvas.Height,
            profile.OutputHeight);
        Assert.Equal(plan.FrameRate.Value, profile.OutputFramesPerSecond);
        Assert.Equal(plan.Encoder, profile.Encoder);
        Assert.Equal(plan.Signal.GpuVendor, profile.GpuVendor);
        Assert.Equal([environment], mediaEvents.Environments);
        Assert.Equal([expectedStatistics], mediaEvents.Statistics);
        Assert.Equal(expectedStatistics, stopped.Statistics);
    }

    [Fact]
    public async Task MediaDiagnosticObserverCannotChangeStartOrStopResult()
    {
        var backend = new ControllableNativeRecordingBackend();
        backend.Session.BlockFirstStop = false;
        backend.Session.Statistics = new RecordingSessionStatistics(
            SourceVideoFrameCount: 120,
            MuxedVideoPacketCount: 90,
            MuxedAudioPacketCount: 142,
            DroppedSourceVideoFrameCount: 0,
            DuplicatedOutputVideoFrameCount: 0,
            LatestEncodeLatency: TimeSpan.Zero,
            MaximumEncodeLatency: TimeSpan.Zero,
            AudioVideoOffset: TimeSpan.Zero);
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink(),
            new ThrowingAudioSessionEventSink(),
            new ThrowingRecordingMediaEventSink());

        var starting = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await starting;
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(CreatePlan().Output, stopped.Recording);
        Assert.Equal(90, stopped.VideoPacketCount);
        Assert.Equal(142, stopped.AudioPacketCount);
        Assert.NotNull(stopped.Statistics);
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

        public void SignalAudioWarning(AudioSessionWarning warning) =>
            (_callbacks ??
             throw new InvalidOperationException("The backend is not open."))
            .AudioWarning!(warning);

        public void SignalAudioStatus(AudioSessionStatus status) =>
            (_callbacks ??
             throw new InvalidOperationException("The backend is not open."))
            .AudioStatus!(status);

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

        public List<AudioRouting> AudioRoutingUpdates { get; } = [];

        public Exception? AbortFailure { get; set; }

        public Exception? StopFailure { get; set; }

        public RecordingSessionStatistics? Statistics { get; set; }

        public bool BlockFirstStop { get; set; } = true;

        public string Id => "native-session-001";

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            AbortCallCount++;
            return AbortFailure is null
                ? Task.CompletedTask
                : Task.FromException(AbortFailure);
        }

        public Task UpdateAudioRoutingAsync(
            AudioRouting routing,
            CancellationToken cancellationToken)
        {
            AudioRoutingUpdates.Add(routing);
            return Task.CompletedTask;
        }

        public async Task<RecordingStopResult> StopAsync(
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            if (StopFailure is not null)
            {
                throw StopFailure;
            }

            if (BlockFirstStop && StopCallCount == 1)
            {
                _firstStopStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new RecordingStopResult(
                new PendingRecording(
                    Path.Combine(Path.GetTempPath(), "take.recording.mp4"),
                    Path.Combine(Path.GetTempPath(), "take.mp4")),
                VideoPacketCount: 90,
                AudioPacketCount: 142,
                Statistics: Statistics);
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

    private sealed class ThrowingAudioSessionEventSink
        : IAudioSessionEventSink
    {
        public List<AudioSessionWarning> Warnings { get; } = [];

        public List<AudioSessionStatus> Statuses { get; } = [];

        public void Publish(AudioSessionWarning warning)
        {
            Warnings.Add(warning);
            throw new InvalidOperationException("warning observer failed");
        }

        public void Publish(AudioSessionStatus status)
        {
            Statuses.Add(status);
            throw new InvalidOperationException("status observer failed");
        }
    }

    private sealed class CapturingRecordingMediaEventSink
        : IRecordingMediaEventSink
    {
        public List<RecordingMediaProfile> Profiles { get; } = [];

        public List<RecordingSessionStatistics> Statistics { get; } = [];

        public List<RecordingEnvironmentSnapshot> Environments { get; } = [];

        public void Publish(RecordingMediaProfile profile) =>
            Profiles.Add(profile);

        public void Publish(RecordingSessionStatistics statistics) =>
            Statistics.Add(statistics);

        public void Publish(RecordingEnvironmentSnapshot environment) =>
            Environments.Add(environment);
    }

    private sealed class StubRecordingEnvironmentSource(
        RecordingEnvironmentSnapshot environment)
        : IRecordingEnvironmentSource
    {
        public RecordingEnvironmentSnapshot Capture(StableVideoSignal signal) =>
            environment;
    }

    private sealed class ThrowingRecordingMediaEventSink
        : IRecordingMediaEventSink
    {
        public void Publish(RecordingMediaProfile profile) =>
            throw new IOException("profile diagnostics unavailable");

        public void Publish(RecordingSessionStatistics statistics) =>
            throw new IOException("statistics diagnostics unavailable");
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
