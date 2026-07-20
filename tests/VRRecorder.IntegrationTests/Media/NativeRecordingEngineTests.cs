using System.Threading.Channels;
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
    public async Task HardwareFailureBeforeFirstPacketRetriesCleanSoftwarePart()
    {
        var hardwarePlan = CreatePlan() with { Encoder = EncoderKind.Nvenc };
        var softwarePlan = hardwarePlan with
        {
            Encoder = EncoderKind.MediaFoundationSoftware,
        };
        var backend = new MultiPartNativeRecordingBackend();
        var rollover = new StubRecordingPartRollover(softwarePlan);
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(2))),
            runtimeFaults,
            rollover);

        var starting = engine.StartAsync(
            hardwarePlan,
            CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFault(
            partIndex: 0,
            new NativeRecordingFault(
                6,
                "NVENC produced no packet",
                NativeRecordingFaultSource.VideoEncoder));
        await backend.WaitUntilOpenedAsync(2);

        Assert.False(starting.IsCompleted);
        Assert.Equal(1, backend.Sessions[0].AbortCallCount);
        Assert.Equal(hardwarePlan, Assert.Single(rollover.StartRetries));
        Assert.Equal(softwarePlan, backend.OpenedPlans[1]);
        Assert.Equal(hardwarePlan.Output, backend.OpenedPlans[1].Output);

        backend.SignalFirstVideoPacketMuxed(partIndex: 1);
        var handle = await starting;

        Assert.Equal("native-part-002", handle.Id);
        Assert.Empty(runtimeFaults.Reports);
    }

    [Fact]
    public async Task AutoNonEncoderFailureBeforeFirstPacketDoesNotRetrySoftware()
    {
        var hardwarePlan = CreatePlan() with { Encoder = EncoderKind.Nvenc };
        var backend = new MultiPartNativeRecordingBackend();
        var rollover = new StubRecordingPartRollover();
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink(),
            rollover);
        var fault = new NativeRecordingFault(
            6,
            "video packet muxing failed before first packet");

        var starting = engine.StartAsync(hardwarePlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFault(partIndex: 0, fault);

        var exception = await Assert.ThrowsAsync<NativeRecordingException>(
            () => starting);

        Assert.Equal(fault, exception.Fault);
        Assert.Single(backend.OpenedPlans);
        Assert.Empty(rollover.StartRetries);
    }

    [Fact]
    public async Task FixedHardwareFailureBeforeFirstPacketDoesNotRetrySoftware()
    {
        var fixedPlan = CreatePlan() with
        {
            Encoder = EncoderKind.Nvenc,
            EncoderPreference = EncoderPreference.Nvenc,
        };
        var backend = new MultiPartNativeRecordingBackend();
        var rollover = new StubRecordingPartRollover();
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink(),
            rollover);

        var starting = engine.StartAsync(fixedPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFault(
            partIndex: 0,
            new NativeRecordingFault(
                6,
                "fixed NVENC produced no packet",
                NativeRecordingFaultSource.VideoEncoder));

        var exception = await Assert.ThrowsAsync<NativeRecordingException>(
            () => starting);

        Assert.Equal("fixed NVENC produced no packet", exception.Fault.Message);
        Assert.Single(backend.OpenedPlans);
        Assert.Empty(rollover.StartRetries);
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
    public async Task SealedHardwareFailureRollsToSoftwarePartWithStableHandle()
    {
        var firstPlan = CreatePlan() with { Encoder = EncoderKind.Nvenc };
        var secondPlan = firstPlan with
        {
            Output = new PendingRecording(
                Path.Combine(Path.GetTempPath(), "take_part002.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "take_part002.mp4")),
            Encoder = EncoderKind.MediaFoundationSoftware,
        };
        var backend = new MultiPartNativeRecordingBackend();
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var rollover = new StubRecordingPartRollover(secondPlan);
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(4))),
            runtimeFaults,
            rollover);

        var starting = engine.StartAsync(firstPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;
        await engine.UpdateAudioRoutingAsync(
            handle,
            AudioRouting.MicOnly,
            CancellationToken.None);

        backend.SignalVideoEncoderFailed(
            partIndex: 0,
            new NativeRecordingFault(6, "NVENC device lost"));
        await backend.WaitUntilOpenedAsync(2);

        Assert.Equal(firstPlan, Assert.Single(rollover.Reservations).Plan);
        Assert.Equal(2, Assert.Single(rollover.Reservations).SegmentNumber);
        Assert.Equal(
            AudioRouting.MicOnly,
            Assert.Single(rollover.Reservations).AudioRouting);
        Assert.Equal(secondPlan, backend.OpenedPlans[1]);
        Assert.False(rollover.FinalizationCompleted.IsCompleted);

        backend.SignalFirstVideoPacketMuxed(partIndex: 1);
        await rollover.FinalizationCompleted;
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(secondPlan.Output, stopped.Recording);
        Assert.Equal(1, backend.Sessions[0].StopCallCount);
        Assert.Equal(1, backend.Sessions[1].StopCallCount);
        Assert.Equal(
            firstPlan.Output,
            Assert.Single(rollover.FinalizedParts).Recording);
        Assert.Empty(runtimeFaults.Reports);
    }

    [Fact]
    public async Task FixedHardwareFailureAfterFirstPacketDoesNotRollToSoftware()
    {
        var fixedPlan = CreatePlan() with
        {
            Encoder = EncoderKind.Nvenc,
            EncoderPreference = EncoderPreference.Nvenc,
        };
        var backend = new MultiPartNativeRecordingBackend();
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var rollover = new StubRecordingPartRollover();
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            runtimeFaults,
            rollover);
        var starting = engine.StartAsync(fixedPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;
        var fault = new NativeRecordingFault(
            6,
            "fixed NVENC failed while recording");

        backend.SignalVideoEncoderFailed(partIndex: 0, fault);

        var report = Assert.Single(runtimeFaults.Reports);
        Assert.Equal(handle, report.Handle);
        Assert.Equal(fault, report.Fault);
        Assert.Single(backend.OpenedPlans);
        Assert.Empty(rollover.Reservations);
        _ = await engine.StopAsync(handle, CancellationToken.None);
    }

    [Fact]
    public async Task StableGeometryUpdatesTheSingleFileLayoutInPlace()
    {
        var plan = CreatePlan();
        var backend = new MultiPartNativeRecordingBackend();
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink());
        var starting = engine.StartAsync(plan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;

        backend.SignalVideoGeometryStable(
            partIndex: 0,
            new VideoGeometry(180, 320, VideoPixelFormat.Rgba8));
        await backend.Sessions[0]
            .WaitUntilVideoLayoutUpdatedAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        var layout = Assert.Single(backend.Sessions[0].VideoLayoutUpdates);
        Assert.Equal(180, layout.Source.Width);
        Assert.Equal(320, layout.Source.Height);
        Assert.Equal(VideoPixelFormat.Rgba8, layout.Source.PixelFormat);
        Assert.Equal(plan.VideoLayout.OutputCanvas, layout.OutputCanvas);
        Assert.Single(backend.Sessions);
        _ = await engine.StopAsync(handle, CancellationToken.None);
    }

    [Fact]
    public async Task StableGeometryCanRollExactPartTwoAndPartThree()
    {
        var firstSignal = new StableVideoSignal(320, 180);
        var secondSignal = firstSignal.WithGeometry(
            new VideoGeometry(640, 360, VideoPixelFormat.Rgba8));
        var thirdSignal = secondSignal.WithGeometry(
            new VideoGeometry(1_280, 720, VideoPixelFormat.Bgra8));
        var firstPlan = ExactPlan(firstSignal, "exact");
        var secondPlan = ExactPlan(secondSignal, "exact_part002");
        var thirdPlan = ExactPlan(thirdSignal, "exact_part003");
        var backend = new MultiPartNativeRecordingBackend();
        var rollover = new StubRecordingPartRollover(secondPlan, thirdPlan);
        rollover.IntermediatePartFinalizing = finalizedPartCount =>
        {
            if (finalizedPartCount == 1)
            {
                backend.SignalVideoGeometryStable(
                    partIndex: 1,
                    new VideoGeometry(
                        1_280,
                        720,
                        VideoPixelFormat.Bgra8));
            }
        };
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            new CapturingRuntimeFaultSink(),
            rollover);
        var starting = engine.StartAsync(firstPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;

        backend.SignalVideoGeometryStable(
            partIndex: 0,
            new VideoGeometry(640, 360, VideoPixelFormat.Rgba8));
        await backend.WaitUntilOpenedAsync(2);
        backend.SignalFirstVideoPacketMuxed(partIndex: 1);
        await rollover.WaitForFinalizedPartAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        await backend.WaitUntilOpenedAsync(3);
        backend.SignalFirstVideoPacketMuxed(partIndex: 2);
        await rollover.WaitForFinalizedPartAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(thirdPlan.Output, stopped.Recording);
        Assert.Equal([2, 3], rollover.ExactReservations
            .Select(item => item.SegmentNumber));
        Assert.Equal([secondSignal, thirdSignal], rollover.ExactReservations
            .Select(item => item.Signal));
        Assert.Equal(1, backend.Sessions[0].StopCallCount);
        Assert.Equal(1, backend.Sessions[1].StopCallCount);
        Assert.Equal(1, backend.Sessions[2].StopCallCount);
        Assert.Equal(2, rollover.FinalizedParts.Count);
    }

    [Fact]
    public async Task FailedSoftwarePartStartLeavesSealedPartStoppable()
    {
        var firstPlan = CreatePlan() with { Encoder = EncoderKind.Nvenc };
        var secondPlan = firstPlan with
        {
            Output = new PendingRecording(
                Path.Combine(Path.GetTempPath(), "take_part002.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "take_part002.mp4")),
            Encoder = EncoderKind.MediaFoundationSoftware,
        };
        var backend = new MultiPartNativeRecordingBackend
        {
            OpenFailureAfterFirst = new IOException(
                "software encoder could not start"),
        };
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var rollover = new StubRecordingPartRollover(secondPlan);
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            runtimeFaults,
            rollover);
        var starting = engine.StartAsync(firstPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;

        backend.SignalVideoEncoderFailed(
            partIndex: 0,
            new NativeRecordingFault(6, "NVENC device lost"));
        var report = await runtimeFaults.WaitForReportAsync();
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(firstPlan.Output, stopped.Recording);
        Assert.Equal(1, backend.Sessions[0].StopCallCount);
        Assert.Empty(rollover.FinalizedParts);
        Assert.Equal(handle, report.Handle);
        Assert.Contains("Software encoder rollover failed", report.Fault.Message);
    }

    [Fact]
    public async Task PreCommitEncoderFailuresCoalesceIntoOneSoftwareRollover()
    {
        var firstPlan = CreatePlan() with { Encoder = EncoderKind.Nvenc };
        var secondPlan = firstPlan with
        {
            Output = new PendingRecording(
                Path.Combine(Path.GetTempPath(), "early_part002.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "early_part002.mp4")),
            Encoder = EncoderKind.MediaFoundationSoftware,
        };
        var backend = new MultiPartNativeRecordingBackend();
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var rollover = new StubRecordingPartRollover(secondPlan);
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            runtimeFaults,
            rollover);
        var starting = engine.StartAsync(firstPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);

        backend.SignalVideoEncoderFailed(
            partIndex: 0,
            new NativeRecordingFault(6, "first pre-commit failure"));
        backend.SignalVideoEncoderFailed(
            partIndex: 0,
            new NativeRecordingFault(6, "duplicate pre-commit failure"));
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;
        await backend.WaitUntilOpenedAsync(2);

        Assert.Single(rollover.Reservations);
        backend.SignalFirstVideoPacketMuxed(partIndex: 1);
        await rollover.FinalizationCompleted.WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(secondPlan.Output, stopped.Recording);
        Assert.Empty(runtimeFaults.Reports);
    }

    [Fact]
    public async Task RolloverRejectsAReservedHardwareEncoderPlan()
    {
        var firstPlan = CreatePlan() with { Encoder = EncoderKind.Nvenc };
        var invalidNextPlan = firstPlan with
        {
            Output = new PendingRecording(
                Path.Combine(Path.GetTempPath(), "invalid_part002.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "invalid_part002.mp4")),
            Encoder = EncoderKind.Amf,
        };
        var backend = new MultiPartNativeRecordingBackend();
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var rollover = new StubRecordingPartRollover(invalidNextPlan);
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            runtimeFaults,
            rollover);
        var starting = engine.StartAsync(firstPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;

        backend.SignalVideoEncoderFailed(
            partIndex: 0,
            new NativeRecordingFault(6, "NVENC device lost"));
        var report = await runtimeFaults.WaitForReportAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(firstPlan.Output, stopped.Recording);
        Assert.Single(backend.Sessions);
        Assert.Equal(handle, report.Handle);
        Assert.Contains("InvalidOperationException", report.Fault.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOpenedSoftwarePartIsAbortedAndSealedPartRemainsStoppable(
        bool abortFails)
    {
        var firstPlan = CreatePlan() with { Encoder = EncoderKind.Nvenc };
        var secondPlan = firstPlan with
        {
            Output = new PendingRecording(
                Path.Combine(Path.GetTempPath(), "failed_part002.recording.mp4"),
                Path.Combine(Path.GetTempPath(), "failed_part002.mp4")),
            Encoder = EncoderKind.MediaFoundationSoftware,
        };
        var backend = new MultiPartNativeRecordingBackend();
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var rollover = new StubRecordingPartRollover(secondPlan);
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            runtimeFaults,
            rollover);
        var starting = engine.StartAsync(firstPlan, CancellationToken.None);
        await backend.WaitUntilOpenedAsync(1);
        backend.SignalFirstVideoPacketMuxed(partIndex: 0);
        var handle = await starting;

        backend.SignalVideoEncoderFailed(
            partIndex: 0,
            new NativeRecordingFault(6, "NVENC device lost"));
        await backend.WaitUntilOpenedAsync(2);
        if (abortFails)
        {
            backend.Sessions[1].AbortFailure = new IOException(
                "software abort failed");
        }
        backend.SignalFault(
            partIndex: 1,
            new NativeRecordingFault(6, "software first packet failed"));
        var report = await runtimeFaults.WaitForReportAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopAsync(handle, CancellationToken.None);

        Assert.Equal(firstPlan.Output, stopped.Recording);
        Assert.Equal(1, backend.Sessions[1].AbortCallCount);
        Assert.Equal(handle, report.Handle);
        Assert.Contains("NativeRecordingException", report.Fault.Message);
    }

    [Fact]
    public async Task SealedSoftwareEncoderFailureUsesTerminalFaultPath()
    {
        var backend = new ControllableNativeRecordingBackend();
        var runtimeFaults = new CapturingRuntimeFaultSink();
        var rollover = new StubRecordingPartRollover(CreatePlan());
        var engine = new NativeRecordingEngine(
            backend,
            new ControllableClock(
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)),
            runtimeFaults,
            rollover);
        var starting = engine.StartAsync(CreatePlan(), CancellationToken.None);
        await backend.WaitUntilOpenedAsync();
        backend.SignalFirstVideoPacketMuxed();
        var handle = await starting;
        var fault = new NativeRecordingFault(6, "software encoder failed");

        backend.SignalVideoEncoderFailed(fault);
        var report = await runtimeFaults.WaitForReportAsync();

        Assert.Equal((handle, fault), report);
        Assert.Empty(rollover.Reservations);
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
        new RecordingPlan(
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
            new FrameRate(30))
        {
            EncoderPreference = EncoderPreference.Auto,
        };

    private static RecordingPlan ExactPlan(
        StableVideoSignal signal,
        string fileStem) => new(
        signal,
        new PendingRecording(
            Path.Combine(Path.GetTempPath(), $"{fileStem}.recording.mp4"),
            Path.Combine(Path.GetTempPath(), $"{fileStem}.mp4")),
        new RecordingSessionTimestamp(new DateTimeOffset(
            2026,
            7,
            10,
            12,
            34,
            56,
            TimeSpan.Zero)),
        new FrameRate(30),
        EncoderKind.MediaFoundationSoftware,
        RecordingVideoLayoutSession.StartExactSegment(signal));

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

        public void SignalVideoEncoderFailed(NativeRecordingFault fault) =>
            (_callbacks ??
             throw new InvalidOperationException("The backend is not open."))
            .VideoEncoderFailed!(fault);

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

    private sealed class MultiPartNativeRecordingBackend
        : INativeRecordingBackend
    {
        private readonly object _gate = new();
        private readonly List<NativeRecordingCallbacks> _callbacks = [];
        private readonly List<(int Count, TaskCompletionSource Completion)>
            _openWaiters = [];

        public List<RecordingPlan> OpenedPlans { get; } = [];

        public List<PartNativeRecordingSession> Sessions { get; } = [];

        public Exception? OpenFailureAfterFirst { get; init; }

        public Task<INativeRecordingSession> OpenAsync(
            RecordingPlan plan,
            NativeRecordingCallbacks callbacks,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (Sessions.Count > 0 && OpenFailureAfterFirst is not null)
                {
                    return Task.FromException<INativeRecordingSession>(
                        OpenFailureAfterFirst);
                }

                OpenedPlans.Add(plan);
                _callbacks.Add(callbacks);
                var session = new PartNativeRecordingSession(
                    $"native-part-{Sessions.Count + 1:000}",
                    plan.Output);
                Sessions.Add(session);
                for (var index = _openWaiters.Count - 1; index >= 0; --index)
                {
                    var waiter = _openWaiters[index];
                    if (Sessions.Count >= waiter.Count)
                    {
                        waiter.Completion.TrySetResult();
                        _openWaiters.RemoveAt(index);
                    }
                }

                return Task.FromResult<INativeRecordingSession>(session);
            }
        }

        public async Task WaitUntilOpenedAsync(int count)
        {
            Task opened;
            lock (_gate)
            {
                if (Sessions.Count >= count)
                {
                    return;
                }

                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _openWaiters.Add((count, completion));
                opened = completion.Task;
            }

            await opened.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void SignalFirstVideoPacketMuxed(int partIndex) =>
            _callbacks[partIndex].FirstVideoPacketMuxed();

        public void SignalVideoEncoderFailed(
            int partIndex,
            NativeRecordingFault fault) =>
            _callbacks[partIndex].VideoEncoderFailed!(fault);

        public void SignalVideoGeometryStable(
            int partIndex,
            VideoGeometry geometry) =>
            _callbacks[partIndex].VideoGeometryStable!(geometry);

        public void SignalFault(
            int partIndex,
            NativeRecordingFault fault) =>
            _callbacks[partIndex].Faulted(fault);
    }

    private sealed class PartNativeRecordingSession(
        string id,
        PendingRecording output) : INativeRecordingSession
    {
        public int AbortCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public List<RecordingVideoLayout> VideoLayoutUpdates { get; } = [];

        private readonly TaskCompletionSource _videoLayoutUpdated = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? AbortFailure { get; set; }

        public string Id { get; } = id;

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            AbortCallCount++;
            return AbortFailure is null
                ? Task.CompletedTask
                : Task.FromException(AbortFailure);
        }

        public Task UpdateAudioRoutingAsync(
            AudioRouting routing,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateVideoLayoutAsync(
            RecordingVideoLayout layout,
            CancellationToken cancellationToken)
        {
            VideoLayoutUpdates.Add(layout);
            _videoLayoutUpdated.TrySetResult();
            return Task.CompletedTask;
        }

        public Task<RecordingStopResult> StopAsync(
            CancellationToken cancellationToken)
        {
            StopCallCount++;
            return Task.FromResult(new RecordingStopResult(
                output,
                VideoPacketCount: 45,
                AudioPacketCount: 72));
        }

        public Task WaitUntilVideoLayoutUpdatedAsync() =>
            _videoLayoutUpdated.Task;
    }

    private sealed class StubRecordingPartRollover
        : IRecordingPartRollover
    {
        private readonly Queue<RecordingPlan> _nextPlans;
        private readonly TaskCompletionSource _finalizationCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Channel<bool> _finalizedParts =
            Channel.CreateUnbounded<bool>();

        public StubRecordingPartRollover(params RecordingPlan[] nextPlans)
        {
            _nextPlans = new Queue<RecordingPlan>(nextPlans);
        }

        public List<(RecordingPlan Plan, int SegmentNumber, AudioRouting AudioRouting)>
            Reservations { get; } = [];

        public List<RecordingStopResult> FinalizedParts { get; } = [];

        public List<RecordingPlan> StartRetries { get; } = [];

        public Action<int>? IntermediatePartFinalizing { get; set; }

        public List<(
            RecordingPlan Plan,
            StableVideoSignal Signal,
            int SegmentNumber,
            AudioRouting AudioRouting)> ExactReservations { get; } = [];

        public Task FinalizationCompleted => _finalizationCompleted.Task;

        public Task<RecordingPlan> ReserveNextSoftwarePartAsync(
            RecordingPlan currentPlan,
            int segmentNumber,
            AudioRouting audioRouting,
            CancellationToken cancellationToken)
        {
            Reservations.Add((currentPlan, segmentNumber, audioRouting));
            return Task.FromResult(_nextPlans.Dequeue());
        }

        public Task<RecordingPlan> ReserveNextExactPartAsync(
            RecordingPlan currentPlan,
            StableVideoSignal nextSignal,
            int segmentNumber,
            AudioRouting audioRouting,
            CancellationToken cancellationToken)
        {
            ExactReservations.Add((
                currentPlan,
                nextSignal,
                segmentNumber,
                audioRouting));
            return Task.FromResult(_nextPlans.Dequeue());
        }

        public Task FinalizeIntermediatePartAsync(
            RecordingStopResult stopped,
            CancellationToken cancellationToken)
        {
            FinalizedParts.Add(stopped);
            IntermediatePartFinalizing?.Invoke(FinalizedParts.Count);
            _finalizationCompleted.TrySetResult();
            _finalizedParts.Writer.TryWrite(true);
            return Task.CompletedTask;
        }

        public Task<bool> WaitForFinalizedPartAsync() =>
            _finalizedParts.Reader.ReadAsync().AsTask();

        public Task<RecordingPlan> PrepareSoftwareStartRetryAsync(
            RecordingPlan failedPlan,
            CancellationToken cancellationToken)
        {
            StartRetries.Add(failedPlan);
            return Task.FromResult(_nextPlans.Dequeue());
        }
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
        private readonly TaskCompletionSource<(
            RecordingHandle Handle,
            NativeRecordingFault Fault)> _reported = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(RecordingHandle Handle, NativeRecordingFault Fault)>
            Reports
        { get; } = [];

        public void Report(
            RecordingHandle handle,
            NativeRecordingFault fault)
        {
            Reports.Add((handle, fault));
            _reported.TrySetResult((handle, fault));
        }

        public Task<(RecordingHandle Handle, NativeRecordingFault Fault)>
            WaitForReportAsync() => _reported.Task;
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
