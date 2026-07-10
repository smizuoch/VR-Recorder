using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.IntegrationTests.Recording;

public sealed class SingleFileFitVideoLayoutIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-005")]
    [Trait("Scenario", "IT-008")]
    public async Task StableTextureChangesFitInsideOnePaddedCanvasAndOutput()
    {
        var reservation = new CapturingRecordingFileReservation();
        var engine = new CapturingRecordingEngine();
        var useCase = new StartRecordingUseCase(
            new StableSignalGateway(new StableVideoSignal(1081, 1921)),
            new ImmediateCountdownTimer(),
            reservation,
            new FixedWallClock(),
            new SufficientStorageProbe(),
            new EncoderSelector(new SoftwareEncoderProbe()),
            engine,
            new NoOpSessionActivator(),
            new NoOpStorageMonitor(),
            new AutoStopScheduler(
                new UnexpectedDelayClock(),
                new UnexpectedStopRequestSink()));

        var result = await useCase.ExecuteAsync(
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(Path.GetTempPath()),
                new FrameRate(30),
                ResolutionChangePolicy: ResolutionChangePolicy.SingleFileFit),
            CancellationToken.None);

        Assert.IsType<StartRecordingResult.Started>(result);
        var plan = Assert.IsType<RecordingPlan>(engine.Plan);
        var initial = plan.VideoLayout.CurrentLayout;
        Assert.Equal(
            new VideoGeometry(1081, 1921, VideoPixelFormat.Bgra8),
            initial.Source);
        Assert.Equal(VideoOrientation.Portrait, initial.Source.Orientation);
        Assert.Equal(
            new VideoGeometry(1082, 1922, VideoPixelFormat.Nv12),
            initial.OutputCanvas);
        Assert.Equal(
            VideoContainCalculator.Calculate(
                initial.Source,
                initial.OutputCanvas),
            initial.Placement);
        Assert.Equal(0, initial.Placement.Width % 2);
        Assert.Equal(0, initial.Placement.Height % 2);
        Assert.Equal(VideoCanvasBackground.Black, initial.Background);
        Assert.Equal(VideoRotation.None, initial.Rotation);

        var landscape = plan.VideoLayout.ApplyStableSignal(
            new StableVideoSignal(1921, 1081));

        Assert.Equal(VideoOrientation.Landscape, landscape.Source.Orientation);
        Assert.Equal(initial.OutputCanvas, landscape.OutputCanvas);
        Assert.Equal(
            new VideoPlacement(0, 657, 1082, 608),
            landscape.Placement);
        Assert.Equal(VideoCanvasBackground.Black, landscape.Background);
        Assert.Equal(VideoRotation.None, landscape.Rotation);

        var portrait = plan.VideoLayout.ApplyStableSignal(
            new StableVideoSignal(721, 1281));

        Assert.Equal(VideoOrientation.Portrait, portrait.Source.Orientation);
        Assert.Equal(initial.OutputCanvas, portrait.OutputCanvas);
        Assert.Equal(
            new VideoPlacement(1, 0, 1080, 1922),
            portrait.Placement);
        Assert.Equal(VideoCanvasBackground.Black, portrait.Background);
        Assert.Equal(VideoRotation.None, portrait.Rotation);
        Assert.Equal(portrait, plan.VideoLayout.CurrentLayout);

        var returnedToInitialDimensions =
            plan.VideoLayout.ApplyStableSignal(
                new StableVideoSignal(1081, 1921));

        Assert.Equal(initial.Source, returnedToInitialDimensions.Source);
        Assert.Equal(initial.OutputCanvas, returnedToInitialDimensions.OutputCanvas);
        Assert.Equal(
            VideoContainCalculator.Calculate(
                returnedToInitialDimensions.Source,
                returnedToInitialDimensions.OutputCanvas),
            returnedToInitialDimensions.Placement);
        Assert.Equal(0, returnedToInitialDimensions.Placement.Width % 2);
        Assert.Equal(0, returnedToInitialDimensions.Placement.Height % 2);
        Assert.Equal(
            VideoCanvasBackground.Black,
            returnedToInitialDimensions.Background);
        Assert.Equal(VideoRotation.None, returnedToInitialDimensions.Rotation);
        Assert.Equal(
            returnedToInitialDimensions,
            plan.VideoLayout.CurrentLayout);

        var descriptor = Assert.Single(reservation.Descriptors);
        Assert.Equal(initial.OutputCanvas.Width, descriptor.Width);
        Assert.Equal(initial.OutputCanvas.Height, descriptor.Height);
        Assert.Equal(1, descriptor.SegmentNumber);
        Assert.DoesNotContain("_part002", plan.Output.TemporaryPath);
        Assert.Equal(1, reservation.CallCount);
        Assert.Equal(1, engine.StartCallCount);
        Assert.Same(plan, engine.Plan);
    }

    [Fact]
    [Trait("Scenario", "IT-009-Deferred")]
    public async Task ExactFollowSegmentsIsRejectedBeforeOutputOrMediaStart()
    {
        var reservation = new CapturingRecordingFileReservation();
        var engine = new CapturingRecordingEngine();
        var useCase = new StartRecordingUseCase(
            new StableSignalGateway(new StableVideoSignal(1920, 1080)),
            new ImmediateCountdownTimer(),
            reservation,
            new FixedWallClock(),
            new SufficientStorageProbe(),
            new EncoderSelector(new SoftwareEncoderProbe()),
            engine,
            new NoOpSessionActivator(),
            new NoOpStorageMonitor(),
            new AutoStopScheduler(
                new UnexpectedDelayClock(),
                new UnexpectedStopRequestSink()));

        var exception = await Assert.ThrowsAsync<
            UnsupportedResolutionChangePolicyException>(() =>
            useCase.ExecuteAsync(
                new StartRecordingCommand(
                    SelfTimer.FromSeconds(0),
                    RecordingDuration.Infinite,
                    new OutputPath(Path.GetTempPath()),
                    new FrameRate(30),
                    ResolutionChangePolicy:
                        ResolutionChangePolicy.ExactFollowSegments),
                CancellationToken.None));

        Assert.Equal(
            ResolutionChangePolicy.ExactFollowSegments,
            exception.Policy);
        Assert.Equal(0, reservation.CallCount);
        Assert.Empty(reservation.Descriptors);
        Assert.Equal(0, engine.StartCallCount);
        Assert.Null(engine.Plan);
    }

    private sealed class StableSignalGateway(StableVideoSignal signal)
        : IVideoSignalGateway
    {
        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(signal);
        }
    }

    private sealed class ImmediateCountdownTimer : ICountdownTimer
    {
        public Task WaitAsync(
            SelfTimer timer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingRecordingFileReservation
        : IRecordingFileReservation
    {
        public int CallCount { get; private set; }

        public List<RecordingFileDescriptor> Descriptors { get; } = [];

        public Task<PendingRecording> ReserveAsync(
            OutputPath outputPath,
            RecordingFileDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Descriptors.Add(descriptor);
            return Task.FromResult(new PendingRecording(
                Path.Combine(outputPath.FullPath, "portrait.recording.mp4"),
                Path.Combine(outputPath.FullPath, "portrait.mp4")));
        }
    }

    private sealed class FixedWallClock : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = new(
            2026,
            7,
            10,
            12,
            34,
            56,
            TimeSpan.FromHours(9));
    }

    private sealed class SufficientStorageProbe : IStorageSpaceProbe
    {
        public Task<StorageSpace> MeasureAsync(
            OutputPath outputPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new StorageSpace(
                StorageCapacityPolicy.MinimumStartBytes));
        }
    }

    private sealed class SoftwareEncoderProbe : IEncoderProbe
    {
        public Task<EncoderProbeResult> ProbeAsync(
            EncoderKind encoder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(EncoderKind.MediaFoundationSoftware, encoder);
            return Task.FromResult(EncoderProbeResult.PacketProduced);
        }
    }

    private sealed class CapturingRecordingEngine : IRecordingEngine
    {
        public int StartCallCount { get; private set; }

        public RecordingPlan? Plan { get; private set; }

        public Task<RecordingHandle> StartAsync(
            RecordingPlan plan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCallCount++;
            Plan = plan;
            return Task.FromResult(new RecordingHandle(
                "single-file-fit-session",
                MonotonicTimestamp.FromElapsed(TimeSpan.Zero)));
        }

        public Task<RecordingStopResult> StopAsync(
            RecordingHandle handle,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Stop was not expected.");
    }

    private sealed class NoOpSessionActivator : IRecordingSessionActivator
    {
        public void Activate(
            RecordingHandle handle,
            CancellationToken sessionLifetimeToken,
            IRecordingSessionCompletionSink? completionSink = null)
        {
        }
    }

    private sealed class NoOpStorageMonitor : IRecordingStorageMonitor
    {
        public Task RunAsync(
            RecordingHandle handle,
            OutputPath outputPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class UnexpectedDelayClock : IMonotonicClock
    {
        public MonotonicTimestamp Now =>
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero);

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A delay was not expected.");
    }

    private sealed class UnexpectedStopRequestSink : IStopRequestSink
    {
        public Task RequestStopAsync(
            RecordingStopRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A stop was not expected.");
    }
}
