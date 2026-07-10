using System.Threading.Channels;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Camera;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class NativeMediaPreflightContractIntegrationTests
{
    [Fact]
    public async Task LifecycleCapturesBaselineBeforeEnablingStreamingAndObservingFrames()
    {
        var events = new List<string>();
        var source = new ControllableSpoutVideoSource([], events);
        var signalGateway = new SpoutVideoSignalGateway(source);
        var probe = new CapturingEncoderProbe();
        var engine = new CapturingRecordingEngine();
        var candidate = new VrChatInstanceCandidate(
            "contract-vrchat",
            "VRChat contract instance",
            new Uri("http://127.0.0.1:19001/"),
            "127.0.0.1",
            9001);
        var camera = new OrderedCameraGateway(events);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new FixedVrChatDiscovery(candidate)),
            new FixedCameraGatewayFactory(camera));
        using var lifecycle = new RecordingLifecycleController(
            connections,
            new NoOpCameraLeaseStore(),
            CreateUseCase(signalGateway, probe, engine),
            new NoOpStopRequestSink(),
            new NoOpCameraRestoreWarningSink());

        var starting = lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);
        await source.WaitUntilObservingAsync().WaitAsync(TimeSpan.FromSeconds(1));

        source.Publish(Frame(1, 1_000, AdapterLuid));
        source.Publish(Frame(2, 1_150, AdapterLuid));
        source.Publish(Frame(3, 1_300, AdapterLuid));
        var result = await starting;

        Assert.IsType<StartRecordingResult.Started>(result.Recording);
        Assert.True(
            events.IndexOf("signal:baseline") <
            events.IndexOf("camera:streaming:true"));
        Assert.True(
            events.IndexOf("camera:streaming:true") <
            events.IndexOf("signal:observe"));
        Assert.Equal(1, events.Count(item => item == "signal:baseline"));
    }

    [Fact]
    public async Task NewStableSenderFlowsToSameAdapterProbeAndRecordingPlan()
    {
        const ulong adapterLuid = AdapterLuid;
        var source = new ControllableSpoutVideoSource(
            [new SpoutSenderSnapshot("pre-start-desktop", 41)]);
        var signalGateway = new SpoutVideoSignalGateway(source);
        var probe = new CapturingEncoderProbe();
        var engine = new CapturingRecordingEngine();
        var useCase = new StartRecordingUseCase(
            signalGateway,
            new ImmediateCountdownTimer(),
            new FixedRecordingFileReservation(),
            new FixedWallClock(),
            new SufficientStorageSpaceProbe(),
            new EncoderSelector(probe),
            engine,
            new NoOpSessionActivator(),
            new NoOpStorageMonitor(),
            new AutoStopScheduler(
                new FixedMonotonicClock(),
                new NoOpStopRequestSink()));
        var command = new StartRecordingCommand(
            SelfTimer.FromSeconds(0),
            RecordingDuration.Infinite,
            new OutputPath(Path.GetTempPath()),
            new FrameRate(60),
            EncoderPreference.Auto,
            GpuVendor.Unknown,
            ResolutionChangePolicy.SingleFileFit);

        var starting = useCase.ExecuteAsync(command, CancellationToken.None);
        await source.WaitUntilObservingAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, source.SnapshotCallCount);
        var first = Frame(
            sequence: 1,
            receivedAtMilliseconds: 1_000,
            adapterLuid);
        var second = Frame(
            sequence: 2,
            receivedAtMilliseconds: 1_299,
            adapterLuid);
        var third = Frame(
            sequence: 3,
            receivedAtMilliseconds: 1_300,
            adapterLuid);

        source.Publish(first);
        source.Publish(second);
        await Task.Yield();
        Assert.False(starting.IsCompleted);

        source.Publish(third);
        var result = Assert.IsType<StartRecordingResult.Started>(await starting);
        var request = Assert.Single(probe.Requests);
        var plan = Assert.Single(engine.Plans);

        Assert.Equal(EncoderKind.Nvenc, request.Encoder);
        Assert.Equal(adapterLuid, request.AdapterLuid);
        Assert.Equal("NVIDIA RTX contract adapter", request.GpuIdentity);
        Assert.Equal("VRChat-Spout-new", plan.Signal.SenderId);
        Assert.Equal(adapterLuid, plan.Signal.AdapterLuid);
        Assert.Equal(VideoPixelFormat.Rgba8, plan.Signal.PixelFormat);
        Assert.Equal(59.94, plan.Signal.EstimatedSourceFramesPerSecond);
        Assert.Equal("VRChat-Spout-new", plan.Media.SpoutSenderIdentity);
        Assert.Equal(adapterLuid, plan.Media.SpoutAdapterLuid);
        Assert.Equal(adapterLuid, plan.Media.EncoderAdapterLuid);
        Assert.Equal("NVIDIA RTX contract adapter", plan.Media.GpuIdentity);
        Assert.Equal(VideoPixelFormat.Rgba8, plan.VideoLayout.CurrentLayout.Source.PixelFormat);
        Assert.Equal("contract-session", result.Handle.Id);
    }

    [Fact]
    public async Task BaselineSenderRequiresANewerFrameGeneration()
    {
        var source = new ControllableSpoutVideoSource(
            [new SpoutSenderSnapshot("VRChat-Spout-new", 41)]);
        var gateway = new SpoutVideoSignalGateway(source);
        await gateway.CaptureBaselineAsync(CancellationToken.None);
        var waiting = gateway.WaitForStableSignalAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        await source.WaitUntilObservingAsync().WaitAsync(TimeSpan.FromSeconds(1));

        source.Publish(Frame(41, 1_000, AdapterLuid));
        source.Publish(Frame(41, 1_300, AdapterLuid));
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        source.Publish(Frame(42, 2_000, AdapterLuid));
        source.Publish(Frame(43, 2_150, AdapterLuid));
        source.Publish(Frame(44, 2_300, AdapterLuid));

        Assert.Equal("VRChat-Spout-new", (await waiting).SenderId);
    }

    [Theory]
    [InlineData("sender")]
    [InlineData("dimensions")]
    [InlineData("adapter")]
    [InlineData("gpuIdentity")]
    [InlineData("gpuVendor")]
    [InlineData("pixelFormat")]
    [InlineData("sourceFps")]
    public async Task CandidateStabilityRestartsWhenItsSignatureChanges(
        string changedField)
    {
        var source = new ControllableSpoutVideoSource([]);
        var gateway = new SpoutVideoSignalGateway(source);
        await gateway.CaptureBaselineAsync(CancellationToken.None);
        var waiting = gateway.WaitForStableSignalAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        await source.WaitUntilObservingAsync().WaitAsync(TimeSpan.FromSeconds(1));

        source.Publish(Frame(1, 1_000, AdapterLuid));
        source.Publish(Frame(2, 1_200, AdapterLuid));
        var changed = changedField switch
        {
            "sender" => Frame(
                1,
                1_300,
                AdapterLuid,
                senderId: "VRChat-Spout-replacement"),
            "dimensions" => Frame(
                3,
                1_300,
                AdapterLuid,
                width: 1280,
                height: 720),
            "adapter" => Frame(3, 1_300, AdapterLuid + 1),
            "gpuIdentity" => Frame(
                3,
                1_300,
                AdapterLuid,
                gpuIdentity: "NVIDIA replacement adapter"),
            "gpuVendor" => Frame(
                3,
                1_300,
                AdapterLuid,
                gpuVendor: GpuVendor.Amd),
            "pixelFormat" => Frame(
                3,
                1_300,
                AdapterLuid,
                pixelFormat: VideoPixelFormat.Bgra8),
            "sourceFps" => Frame(
                3,
                1_300,
                AdapterLuid,
                estimatedSourceFramesPerSecond: 30),
            _ => throw new InvalidOperationException(
                $"Unsupported changed field {changedField}."),
        };
        source.Publish(changed);
        source.Publish(changed with
        {
            FrameSequence = changed.FrameSequence + 1,
            ReceivedAt = MonotonicTimestamp.FromElapsed(
                TimeSpan.FromMilliseconds(1_599)),
        });
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        source.Publish(changed with
        {
            FrameSequence = changed.FrameSequence + 2,
            ReceivedAt = MonotonicTimestamp.FromElapsed(
                TimeSpan.FromMilliseconds(1_600)),
        });

        Assert.Equal(changed.Signal.SenderId, (await waiting).SenderId);
    }

    [Fact]
    public async Task DuplicateFrameSequenceRestartsCandidateStability()
    {
        var source = new ControllableSpoutVideoSource([]);
        var gateway = new SpoutVideoSignalGateway(source);
        await gateway.CaptureBaselineAsync(CancellationToken.None);
        var waiting = gateway.WaitForStableSignalAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        await source.WaitUntilObservingAsync().WaitAsync(TimeSpan.FromSeconds(1));

        source.Publish(Frame(1, 1_000, AdapterLuid));
        source.Publish(Frame(2, 1_200, AdapterLuid));
        source.Publish(Frame(2, 1_300, AdapterLuid));
        source.Publish(Frame(3, 1_400, AdapterLuid));
        source.Publish(Frame(4, 1_699, AdapterLuid));
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        source.Publish(Frame(5, 1_700, AdapterLuid));

        Assert.Equal(5ul, source.LastPublishedSequence);
        Assert.Equal("VRChat-Spout-new", (await waiting).SenderId);
    }

    [Fact]
    public async Task NonMonotonicFrameTimestampRestartsCandidateStability()
    {
        var source = new ControllableSpoutVideoSource([]);
        var gateway = new SpoutVideoSignalGateway(source);
        await gateway.CaptureBaselineAsync(CancellationToken.None);
        var waiting = gateway.WaitForStableSignalAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        await source.WaitUntilObservingAsync().WaitAsync(TimeSpan.FromSeconds(1));

        source.Publish(Frame(1, 1_000, AdapterLuid));
        source.Publish(Frame(2, 1_200, AdapterLuid));
        source.Publish(Frame(3, 1_100, AdapterLuid));
        source.Publish(Frame(4, 1_400, AdapterLuid));
        source.Publish(Frame(5, 1_699, AdapterLuid));
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        source.Publish(Frame(6, 1_700, AdapterLuid));

        Assert.Equal("VRChat-Spout-new", (await waiting).SenderId);
    }

    [Fact]
    public async Task BaselineFailureRestoresReadyBeforeAnyCameraWrite()
    {
        var events = new List<string>();
        var candidate = ContractCandidate("baseline-failure");
        var lifecycle = CreateLifecycle(
            candidate,
            new ThrowingBaselineVideoSignalGateway(
                new InvalidOperationException("baseline failed")),
            events);
        using (lifecycle)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                lifecycle.StartAsync(
                    candidate.ServiceId,
                    StartCommand(),
                    CancellationToken.None));

            Assert.Equal("baseline failed", failure.Message);
            Assert.Equal(RecorderState.Ready, lifecycle.State);
            Assert.Empty(events);
        }
    }

    [Fact]
    public async Task BaselineCancellationRestoresNoSignalBeforeAnyRetryCameraWrite()
    {
        var events = new List<string>();
        var candidate = ContractCandidate("baseline-cancel");
        var signal = new CancelingRetryBaselineVideoSignalGateway();
        using var lifecycle = CreateLifecycle(candidate, signal, events);
        var first = await lifecycle.StartAsync(
            candidate.ServiceId,
            StartCommand(),
            CancellationToken.None);
        Assert.IsType<StartRecordingResult.NoSignal>(first.Recording);
        Assert.Equal(RecorderState.NoSignal, lifecycle.State);
        events.Clear();
        using var cancellation = new CancellationTokenSource();
        signal.CancelRetry = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            lifecycle.StartAsync(
                candidate.ServiceId,
                StartCommand(),
                cancellation.Token));

        Assert.Equal(RecorderState.NoSignal, lifecycle.State);
        Assert.Empty(events);
    }

    [Fact]
    public async Task NoStableSenderTimesOutButCallerCancellationStaysCancellation()
    {
        var timeoutSource = new ControllableSpoutVideoSource([]);
        var timeoutGateway = new SpoutVideoSignalGateway(timeoutSource);
        await timeoutGateway.CaptureBaselineAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            timeoutGateway.WaitForStableSignalAsync(
                TimeSpan.FromMilliseconds(30),
                CancellationToken.None));

        var canceledSource = new ControllableSpoutVideoSource([]);
        var canceledGateway = new SpoutVideoSignalGateway(canceledSource);
        await canceledGateway.CaptureBaselineAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var canceled = canceledGateway.WaitForStableSignalAsync(
            TimeSpan.FromSeconds(1),
            cancellation.Token);
        await canceledSource.WaitUntilObservingAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
    }

    [Fact]
    public void StableSignalRejectsInvalidSourceMetadata()
    {
        Assert.Throws<ArgumentException>(() => Signal(senderId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Signal(adapterLuid: 0));
        Assert.Throws<ArgumentException>(() => Signal(gpuIdentity: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Signal(width: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Signal(height: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Signal(
            pixelFormat: (VideoPixelFormat)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => Signal(
            estimatedSourceFramesPerSecond: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Signal(
            estimatedSourceFramesPerSecond: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Signal(
            estimatedSourceFramesPerSecond: 1_001));
    }

    [Fact]
    public async Task EveryFallbackProbeRetainsTheStableSenderAdapter()
    {
        var probe = new FallbackEncoderProbe();
        var selector = new EncoderSelector(probe);
        var signal = Signal();

        var selected = await selector.SelectAsync(
            EncoderPreference.Auto,
            signal,
            CancellationToken.None);

        Assert.Equal(EncoderKind.MediaFoundationSoftware, selected);
        Assert.Equal(
            [EncoderKind.Nvenc, EncoderKind.MediaFoundationSoftware],
            probe.Requests.Select(request => request.Encoder));
        Assert.All(
            probe.Requests,
            request => Assert.Equal(AdapterLuid, request.AdapterLuid));
        Assert.All(
            probe.Requests,
            request => Assert.Equal(signal.GpuIdentity, request.GpuIdentity));
    }

    private const ulong AdapterLuid = 0x00000001ABCDEF01;

    private static VrChatInstanceCandidate ContractCandidate(string suffix) =>
        new(
            $"contract-{suffix}",
            $"VRChat contract {suffix}",
            new Uri("http://127.0.0.1:19001/"),
            "127.0.0.1",
            9001);

    private static RecordingLifecycleController CreateLifecycle(
        VrChatInstanceCandidate candidate,
        IVideoSignalGateway signalGateway,
        List<string> cameraEvents)
    {
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new FixedVrChatDiscovery(candidate)),
            new FixedCameraGatewayFactory(
                new OrderedCameraGateway(cameraEvents)));
        return new RecordingLifecycleController(
            connections,
            new NoOpCameraLeaseStore(),
            CreateUseCase(
                signalGateway,
                new CapturingEncoderProbe(),
                new CapturingRecordingEngine()),
            new NoOpStopRequestSink(),
            new NoOpCameraRestoreWarningSink());
    }

    private static StartRecordingCommand StartCommand() =>
        new(
            SelfTimer.FromSeconds(0),
            RecordingDuration.Infinite,
            new OutputPath(Path.GetTempPath()),
            new FrameRate(60),
            EncoderPreference.Auto,
            GpuVendor.Unknown,
            ResolutionChangePolicy.SingleFileFit);

    private static StartRecordingUseCase CreateUseCase(
        IVideoSignalGateway signalGateway,
        IEncoderProbe probe,
        IRecordingEngine engine) =>
        new(
            signalGateway,
            new ImmediateCountdownTimer(),
            new FixedRecordingFileReservation(),
            new FixedWallClock(),
            new SufficientStorageSpaceProbe(),
            new EncoderSelector(probe),
            engine,
            new NoOpSessionActivator(),
            new NoOpStorageMonitor(),
            new AutoStopScheduler(
                new FixedMonotonicClock(),
                new NoOpStopRequestSink()));

    private static StableVideoSignal Signal(
        string senderId = "VRChat-Spout-new",
        ulong adapterLuid = AdapterLuid,
        string gpuIdentity = "NVIDIA RTX contract adapter",
        GpuVendor gpuVendor = GpuVendor.Nvidia,
        int width = 1920,
        int height = 1080,
        VideoPixelFormat pixelFormat = VideoPixelFormat.Rgba8,
        double estimatedSourceFramesPerSecond = 59.94) =>
        new(
            senderId,
            adapterLuid,
            gpuIdentity,
            gpuVendor,
            width,
            height,
            pixelFormat,
            estimatedSourceFramesPerSecond);

    private static SpoutFrameObservation Frame(
        ulong sequence,
        double receivedAtMilliseconds,
        ulong adapterLuid,
        string senderId = "VRChat-Spout-new",
        int width = 1920,
        int height = 1080,
        string gpuIdentity = "NVIDIA RTX contract adapter",
        GpuVendor gpuVendor = GpuVendor.Nvidia,
        VideoPixelFormat pixelFormat = VideoPixelFormat.Rgba8,
        double estimatedSourceFramesPerSecond = 59.94) =>
        new(
            Signal(
                senderId,
                adapterLuid,
                gpuIdentity,
                gpuVendor,
                width: width,
                height: height,
                pixelFormat: pixelFormat,
                estimatedSourceFramesPerSecond:
                    estimatedSourceFramesPerSecond),
            sequence,
            MonotonicTimestamp.FromElapsed(
                TimeSpan.FromMilliseconds(receivedAtMilliseconds)));

    private sealed class ControllableSpoutVideoSource(
        IReadOnlyList<SpoutSenderSnapshot> baseline,
        List<string>? events = null) : ISpoutVideoSource
    {
        private readonly Channel<SpoutFrameObservation> _frames =
            Channel.CreateUnbounded<SpoutFrameObservation>();
        private readonly TaskCompletionSource _observing = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int SnapshotCallCount { get; private set; }

        public ulong LastPublishedSequence { get; private set; }

        public Task<IReadOnlyList<SpoutSenderSnapshot>> SnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotCallCount++;
            events?.Add("signal:baseline");
            return Task.FromResult(baseline);
        }

        public async IAsyncEnumerable<SpoutFrameObservation> ObserveFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            events?.Add("signal:observe");
            _observing.TrySetResult();
            await foreach (var frame in _frames.Reader
                               .ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public Task WaitUntilObservingAsync() => _observing.Task;

        public void Publish(SpoutFrameObservation frame)
        {
            LastPublishedSequence = frame.FrameSequence;
            Assert.True(_frames.Writer.TryWrite(frame));
        }
    }

    private sealed class FallbackEncoderProbe : IEncoderProbe
    {
        public List<EncoderProbeRequest> Requests { get; } = [];

        public Task<EncoderProbeResult> ProbeAsync(
            EncoderProbeRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(
                request.Encoder == EncoderKind.MediaFoundationSoftware
                    ? EncoderProbeResult.PacketProduced
                    : EncoderProbeResult.Failed);
        }
    }

    private sealed class ThrowingBaselineVideoSignalGateway(Exception failure)
        : IVideoSignalGateway
    {
        public Task CaptureBaselineAsync(CancellationToken cancellationToken) =>
            Task.FromException(failure);

        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Signal wait was not expected.");
    }

    private sealed class CancelingRetryBaselineVideoSignalGateway
        : IVideoSignalGateway
    {
        private int _captureCount;

        public Action? CancelRetry { get; set; }

        public Task CaptureBaselineAsync(CancellationToken cancellationToken)
        {
            _captureCount++;
            if (_captureCount == 2)
            {
                CancelRetry?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        }

        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromException<StableVideoSignal>(new TimeoutException());
    }

    private sealed class CapturingEncoderProbe : IEncoderProbe
    {
        public List<EncoderProbeRequest> Requests { get; } = [];

        public Task<EncoderProbeResult> ProbeAsync(
            EncoderProbeRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(EncoderProbeResult.PacketProduced);
        }
    }

    private sealed class CapturingRecordingEngine : IRecordingEngine
    {
        public List<RecordingPlan> Plans { get; } = [];

        public Task<RecordingHandle> StartAsync(
            RecordingPlan plan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Plans.Add(plan);
            return Task.FromResult(new RecordingHandle(
                "contract-session",
                MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(2))));
        }

        public Task<RecordingStopResult> StopAsync(
            RecordingHandle handle,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Stop was not expected.");
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

    private sealed class FixedRecordingFileReservation : IRecordingFileReservation
    {
        public Task<PendingRecording> ReserveAsync(
            OutputPath outputPath,
            RecordingFileDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PendingRecording(
                Path.Combine(outputPath.Value, "contract.recording.mp4"),
                Path.Combine(outputPath.Value, "contract.mp4")));
        }
    }

    private sealed class FixedWallClock : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class SufficientStorageSpaceProbe : IStorageSpaceProbe
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

    private sealed class NoOpSessionActivator : IRecordingSessionActivator
    {
        public void Activate(
            RecordingHandle handle,
            CancellationToken sessionLifetimeToken = default,
            IRecordingSessionCompletionSink? completionSink = null)
        {
        }
    }

    private sealed class NoOpStorageMonitor : IRecordingStorageMonitor
    {
        public Task RunAsync(
            RecordingHandle handle,
            OutputPath outputPath,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedMonotonicClock : IMonotonicClock
    {
        public MonotonicTimestamp Now =>
            MonotonicTimestamp.FromElapsed(TimeSpan.Zero);

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpStopRequestSink : IStopRequestSink
    {
        public Task RequestStopAsync(
            RecordingStopRequest request,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedVrChatDiscovery(VrChatInstanceCandidate candidate)
        : IVrChatInstanceDiscovery
    {
        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<VrChatInstanceCandidate>>(
                [candidate]);
        }
    }

    private sealed class FixedCameraGatewayFactory(IVrChatCameraGateway gateway)
        : IVrChatCameraGatewayFactory
    {
        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate) =>
            gateway;
    }

    private sealed class OrderedCameraGateway(List<string> events)
        : IVrChatCameraGateway
    {
        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("camera:snapshot");
            return Task.FromResult(new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)));
        }

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"camera:mode:{mode}");
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"camera:streaming:{enabled.ToString().ToLowerInvariant()}");
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpCameraLeaseStore : ICameraLeaseStore
    {
        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpCameraRestoreWarningSink
        : ICameraRestoreWarningSink
    {
        public Task PublishAsync(
            CameraRestoreWarning warning,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
