using System.Threading.Channels;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class NativeMediaPreflightContractIntegrationTests
{
    [Fact]
    public async Task NewStableSenderFlowsToSameAdapterProbeAndRecordingPlan()
    {
        const ulong adapterLuid = 0x00000001ABCDEF01;
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

    private static SpoutFrameObservation Frame(
        ulong sequence,
        double receivedAtMilliseconds,
        ulong adapterLuid) =>
        new(
            new StableVideoSignal(
                "VRChat-Spout-new",
                adapterLuid,
                "NVIDIA RTX contract adapter",
                GpuVendor.Nvidia,
                1920,
                1080,
                VideoPixelFormat.Rgba8,
                59.94),
            sequence,
            MonotonicTimestamp.FromElapsed(
                TimeSpan.FromMilliseconds(receivedAtMilliseconds)));

    private sealed class ControllableSpoutVideoSource(
        IReadOnlyList<SpoutSenderSnapshot> baseline) : ISpoutVideoSource
    {
        private readonly Channel<SpoutFrameObservation> _frames =
            Channel.CreateUnbounded<SpoutFrameObservation>();
        private readonly TaskCompletionSource _observing = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int SnapshotCallCount { get; private set; }

        public Task<IReadOnlyList<SpoutSenderSnapshot>> SnapshotAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotCallCount++;
            return Task.FromResult(baseline);
        }

        public async IAsyncEnumerable<SpoutFrameObservation> ObserveFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            _observing.TrySetResult();
            await foreach (var frame in _frames.Reader
                               .ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public Task WaitUntilObservingAsync() => _observing.Task;

        public void Publish(SpoutFrameObservation frame) =>
            Assert.True(_frames.Writer.TryWrite(frame));
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
        public MonotonicTimestamp Now => MonotonicTimestamp.Zero;

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
}
