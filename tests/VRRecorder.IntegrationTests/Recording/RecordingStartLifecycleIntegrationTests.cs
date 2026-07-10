using System.Net;
using System.Net.Sockets;
using System.Text;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Camera;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;
using VRRecorder.Infrastructure.Osc;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Recording;

public sealed class RecordingStartLifecycleIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-001")]
    public async Task ExactTargetReportsRecordingOnlyAfterConfirmedCameraAndFirstPacket()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var directory = TemporaryDirectory.Create();
        using var firstOsc = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var selectedOsc = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        var first = Advertisement("start-alpha", httpPort: 19101);
        var selected = Advertisement("start-beta", httpPort: 19102);
        using var http = new HttpMessageInvoker(new OscQueryFixtureHandler(
            new Dictionary<int, OscQueryFixture>
            {
                [first.HttpPort] = new(
                    first.InstanceName,
                    ((IPEndPoint)firstOsc.Client.LocalEndPoint!).Port),
                [selected.HttpPort] = new(
                    selected.InstanceName,
                    ((IPEndPoint)selectedOsc.Client.LocalEndPoint!).Port),
            }));
        var discovery = new OscQueryVrChatInstanceDiscovery(
            new StubOscQueryServiceBrowser([selected, first]),
            http,
            TimeSpan.FromSeconds(1));
        var gatewayFactory = new CapturingGatewayFactory(
            new ConfirmedUdpVrChatCameraGatewayFactory());
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(discovery),
            gatewayFactory);
        var signal = new ControllableStableVideoSignalGateway();
        var clock = new FixedMonotonicClock(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(10)));
        var backend = new ControllableNativeRecordingBackend();
        var engine = new NativeRecordingEngine(
            backend,
            clock,
            new UnexpectedRuntimeFaultSink());
        var sessions = new ActiveRecordingSessionCoordinator(
            engine,
            CreateUnexpectedFinalization());
        var encoderProbe = new SameGpuEncoderProbe();
        var startRecording = new StartRecordingUseCase(
            signal,
            new ImmediateCountdownTimer(),
            new FileSystemRecordingFileReservation(),
            new FixedWallClock(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.FromHours(9))),
            new SufficientStorageSpaceProbe(),
            new EncoderSelector(encoderProbe),
            engine,
            sessions,
            new NoOpStorageMonitor(),
            new AutoStopScheduler(clock, sessions));
        var leaseStore = new CapturingCameraLeaseStore();
        using var lifecycle = new RecordingLifecycleController(
            connections,
            leaseStore,
            startRecording,
            sessions);

        var starting = lifecycle.StartAsync(
            selected.ServiceId,
            new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)),
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(directory.Path),
                new FrameRate(30),
                EncoderPreference.Auto,
                GpuVendor.Nvidia),
            timeout.Token);

        var modeReceive = selectedOsc.ReceiveAsync(timeout.Token).AsTask();
        if (ReferenceEquals(starting, await Task.WhenAny(starting, modeReceive)))
        {
            await starting;
        }

        var mode = await modeReceive;
        Assert.Equal(OscPacketCodec.EncodeMode(CameraMode.Stream), mode.Buffer);
        Assert.Equal(0, firstOsc.Available);
        Assert.Equal(RecorderState.Arming, lifecycle.State);
        await selectedOsc.SendAsync(
            mode.Buffer,
            mode.RemoteEndPoint,
            timeout.Token);
        var streaming = await selectedOsc.ReceiveAsync(timeout.Token);
        Assert.Equal(
            OscPacketCodec.EncodeStreaming(enabled: true),
            streaming.Buffer);
        await selectedOsc.SendAsync(
            streaming.Buffer,
            streaming.RemoteEndPoint,
            timeout.Token);
        await signal.WaitUntilRequestedAsync();

        Assert.Empty(Directory.GetFiles(directory.Path));
        Assert.Equal(0, backend.OpenCallCount);
        Assert.False(starting.IsCompleted);
        Assert.Equal(RecorderState.Arming, lifecycle.State);

        signal.Complete(new StableVideoSignal(320, 180));
        await backend.WaitUntilOpenedAsync();

        Assert.False(starting.IsCompleted);
        Assert.Equal(RecorderState.Arming, lifecycle.State);
        Assert.Equal(RecorderState.Ready, sessions.State);
        Assert.Equal(
            [EncoderKind.Nvenc, EncoderKind.MediaFoundationSoftware],
            encoderProbe.ProbedEncoders);
        Assert.Equal(
            EncoderKind.MediaFoundationSoftware,
            backend.Plan?.Encoder);
        Assert.True(File.Exists(backend.Plan?.Output.TemporaryPath));
        Assert.False(File.Exists(backend.Plan?.Output.FinalPath));
        Assert.Single(Directory.GetFiles(directory.Path));

        backend.CommitFirstVideoPacket();
        var result = await starting;

        var connected = Assert.IsType<
            VrChatCameraConnectionResolution.Connected>(result.Connection);
        await using var gatewayLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(
            connected.Gateway);
        var started = Assert.IsType<StartRecordingResult.Started>(result.Recording);
        Assert.Equal(backend.Session.Id, started.Handle.Id);
        Assert.Equal(RecorderState.Recording, result.State);
        Assert.Equal(RecorderState.Recording, lifecycle.State);
        Assert.Equal(RecorderState.Recording, sessions.State);
        Assert.Equal(new[] { connected.Candidate }, gatewayFactory.CreatedFor);
        Assert.Equal(1, leaseStore.SaveCallCount);
        Assert.Equal(1, backend.OpenCallCount);
        Assert.Equal(0, firstOsc.Available);

        var firstFrameAt = started.Handle.FirstPacketCommittedAt;
        await lifecycle.ObserveFreshVideoFrameAsync(
            new VideoFrameObservation(firstFrameAt, isBlack: true),
            timeout.Token);
        Assert.Equal(
            VideoSignalStatus.Available,
            await lifecycle.EvaluateVideoSignalAsync(
                firstFrameAt.Add(TimeSpan.FromMilliseconds(1499)),
                timeout.Token));
        Assert.Equal(RecorderState.Recording, lifecycle.State);

        Assert.Equal(
            VideoSignalStatus.SignalLost,
            await lifecycle.EvaluateVideoSignalAsync(
                firstFrameAt.Add(TimeSpan.FromMilliseconds(1500)),
                timeout.Token));
        Assert.Equal(RecorderState.SignalLost, lifecycle.State);

        var recoveredAt = firstFrameAt.Add(TimeSpan.FromSeconds(2));
        await lifecycle.ObserveFreshVideoFrameAsync(
            new VideoFrameObservation(recoveredAt, isBlack: true),
            timeout.Token);

        Assert.Equal(RecorderState.Recording, lifecycle.State);
        Assert.Equal(
            VideoSignalStatus.Available,
            await lifecycle.EvaluateVideoSignalAsync(
                recoveredAt,
                timeout.Token));
    }

    private static OscQueryServiceAdvertisement Advertisement(
        string suffix,
        int httpPort)
    {
        var instanceName = $"VRChat-Client-{suffix}";
        return new OscQueryServiceAdvertisement(
            $"{instanceName}._oscjson._tcp.local.",
            instanceName,
            IPAddress.Loopback,
            httpPort);
    }

    private static RecordingFileFinalizationUseCase CreateUnexpectedFinalization() =>
        new(
            new UnexpectedRecordingFileFinalizer(),
            new UnexpectedRecordingFileValidator(),
            new UnexpectedRecordingRecoveryStore(),
            new UnexpectedSavedRecordingSink());

    private sealed class StubOscQueryServiceBrowser(
        IReadOnlyList<OscQueryServiceAdvertisement> advertisements)
        : IOscQueryServiceBrowser
    {
        public Task<IReadOnlyList<OscQueryServiceAdvertisement>> BrowseAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(advertisements);
        }
    }

    private sealed class OscQueryFixtureHandler(
        IReadOnlyDictionary<int, OscQueryFixture> fixtures)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri ??
                      throw new InvalidOperationException(
                          "The OSCQuery request URI is missing.");
            var fixture = fixtures[uri.Port];
            var json = uri.PathAndQuery switch
            {
                "/?HOST_INFO" => $$"""
                    {
                      "HOST_INFO": {
                        "NAME": "{{fixture.Name}}",
                        "OSC_IP": "127.0.0.1",
                        "OSC_PORT": {{fixture.OscPort}},
                        "OSC_TRANSPORT": "UDP"
                      }
                    }
                    """,
                "/usercamera/Mode" => Endpoint(
                    "/usercamera/Mode",
                    "i"),
                "/usercamera/Streaming" => Endpoint(
                    "/usercamera/Streaming",
                    "T"),
                "/usercamera/OrientationIsLandscape" => Endpoint(
                    "/usercamera/OrientationIsLandscape",
                    "T"),
                _ => throw new InvalidOperationException(
                    $"Unexpected OSCQuery request {uri.PathAndQuery}."),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"),
            });
        }

        private static string Endpoint(string path, string type) => $$"""
            {
              "FULL_PATH": "{{path}}",
              "TYPE": "{{type}}",
              "ACCESS": 3
            }
            """;
    }

    private sealed class CapturingGatewayFactory(
        IVrChatCameraGatewayFactory inner) : IVrChatCameraGatewayFactory
    {
        public List<VrChatInstanceCandidate> CreatedFor { get; } = [];

        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate)
        {
            CreatedFor.Add(candidate);
            return inner.Create(candidate);
        }
    }

    private sealed class CapturingCameraLeaseStore : ICameraLeaseStore
    {
        public int SaveCallCount { get; private set; }

        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Camera restore was not expected.");
    }

    private sealed class ControllableStableVideoSignalGateway : IVideoSignalGateway
    {
        private readonly TaskCompletionSource _requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<StableVideoSignal> _signal = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StableVideoSignal> WaitForStableSignalAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            _requested.TrySetResult();
            return _signal.Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilRequestedAsync() => _requested.Task;

        public void Complete(StableVideoSignal signal) =>
            _signal.TrySetResult(signal);
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

    private sealed class FixedWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = localNow;
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

    private sealed class SameGpuEncoderProbe : IEncoderProbe
    {
        public List<EncoderKind> ProbedEncoders { get; } = [];

        public Task<EncoderProbeResult> ProbeAsync(
            EncoderKind encoder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbedEncoders.Add(encoder);
            return Task.FromResult(encoder == EncoderKind.Nvenc
                ? EncoderProbeResult.Failed
                : EncoderProbeResult.PacketProduced);
        }
    }

    private sealed class FixedMonotonicClock(MonotonicTimestamp now)
        : IMonotonicClock
    {
        public MonotonicTimestamp Now { get; } = now;

        public Task DelayUntilAsync(
            MonotonicTimestamp deadline,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A clock delay was not expected.");
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

    private sealed class ControllableNativeRecordingBackend
        : INativeRecordingBackend
    {
        private readonly TaskCompletionSource _opened = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private NativeRecordingCallbacks? _callbacks;

        public int OpenCallCount { get; private set; }

        public RecordingPlan? Plan { get; private set; }

        public ControllableNativeRecordingSession Session { get; } = new();

        public Task<INativeRecordingSession> OpenAsync(
            RecordingPlan plan,
            NativeRecordingCallbacks callbacks,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCallCount++;
            Plan = plan;
            _callbacks = callbacks;
            _opened.TrySetResult();
            return Task.FromResult<INativeRecordingSession>(Session);
        }

        public Task WaitUntilOpenedAsync() => _opened.Task;

        public void CommitFirstVideoPacket() =>
            (_callbacks ?? throw new InvalidOperationException(
                "The native session was not opened."))
            .FirstVideoPacketMuxed();
    }

    private sealed class ControllableNativeRecordingSession
        : INativeRecordingSession
    {
        public string Id => "controlled-native-session-001";

        public Task<RecordingStopResult> StopAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Native stop was not expected.");

        public Task AbortAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class UnexpectedRuntimeFaultSink
        : INativeRecordingRuntimeFaultSink
    {
        public void Report(NativeRecordingFault fault) =>
            throw new InvalidOperationException("A native fault was not expected.");
    }

    private sealed class UnexpectedRecordingFileFinalizer
        : IRecordingFileFinalizer
    {
        public Task<FinalizedRecording> FinalizeAsync(
            PendingRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Finalization was not expected.");
    }

    private sealed class UnexpectedRecordingFileValidator
        : IRecordingFileValidator
    {
        public Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Validation was not expected.");
    }

    private sealed class UnexpectedRecordingRecoveryStore
        : IRecordingRecoveryStore
    {
        public Task<QuarantinedRecording> QuarantineAsync(
            RecoverableRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Recovery was not expected.");
    }

    private sealed class UnexpectedSavedRecordingSink : ISavedRecordingSink
    {
        public Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Saved was not expected.");
    }

    private sealed record OscQueryFixture(string Name, int OscPort);

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-start-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
