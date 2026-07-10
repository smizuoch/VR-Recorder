using System.Runtime.InteropServices;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class PInvokeNativeRecordingBackendTests
{
    [Fact]
    public async Task CompleteMediaCreateContractCrossesManagedNativeAbi()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var signal = new StableVideoSignal(
            "VRChat-Spout-日本語-42",
            0x00000001ABCDEF01,
            "pci\\ven_10de&dev_2684|driver-32.0.15.6094",
            GpuVendor.Nvidia,
            1081,
            1921,
            VideoPixelFormat.Rgba8,
            59.94);
        var layout = RecordingVideoLayoutSession.Start(
            signal,
            ResolutionChangePolicy.SingleFileFit);
        var currentLayout = layout.ApplyStableSignal(
            new StableVideoSignal(
                signal.SenderId,
                signal.AdapterLuid,
                signal.GpuIdentity,
                signal.GpuVendor,
                1921,
                1081,
                signal.PixelFormat,
                signal.EstimatedSourceFramesPerSecond));
        var media = new RecordingMediaConfiguration(
            AudioRouting.MicOnly,
            "{0.0.0.00000000}.desktop-日本語",
            "{0.0.1.00000000}.microphone-日本語",
            desktopGainDb: -12.25,
            microphoneGainDb: 3.0,
            VideoQualityPreset.Standard,
            spoutSenderIdentity: "VRChat-Spout-日本語-42",
            spoutAdapterLuid: 0x00000001ABCDEF01,
            encoderAdapterLuid: 0x00000001ABCDEF01,
            gpuIdentity: "pci\\ven_10de&dev_2684|driver-32.0.15.6094");
        var plan = new RecordingPlan(
            signal,
            pending,
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(60),
            EncoderKind.Nvenc,
            layout)
        {
            Media = media,
        };
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());

        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);
        var observed = controls.MediaConfig();

        Assert.Equal(
            checked((uint)currentLayout.OutputCanvas.Width),
            observed.CanvasWidth);
        Assert.Equal(
            checked((uint)currentLayout.OutputCanvas.Height),
            observed.CanvasHeight);
        Assert.Equal(checked((uint)currentLayout.Source.Width), observed.SourceWidth);
        Assert.Equal(checked((uint)currentLayout.Source.Height), observed.SourceHeight);
        Assert.Equal(
            checked((uint)currentLayout.Placement.OffsetX),
            observed.DestinationX);
        Assert.Equal(
            checked((uint)currentLayout.Placement.OffsetY),
            observed.DestinationY);
        Assert.Equal(
            checked((uint)currentLayout.Placement.Width),
            observed.DestinationWidth);
        Assert.Equal(
            checked((uint)currentLayout.Placement.Height),
            observed.DestinationHeight);
        Assert.Equal(1u, observed.CanvasBackground);
        Assert.Equal(1u, observed.Rotation);
        Assert.Equal(3u, observed.AudioRouting);
        Assert.Equal(1u, observed.QualityPreset);
        Assert.Equal(media.DesktopEndpointId, observed.DesktopEndpointId);
        Assert.Equal(media.MicrophoneEndpointId, observed.MicrophoneEndpointId);
        Assert.Equal(media.DesktopGainDb, observed.DesktopGainDb);
        Assert.Equal(media.MicrophoneGainDb, observed.MicrophoneGainDb);
        Assert.Equal(media.SpoutSenderIdentity, observed.SpoutSenderIdentity);
        Assert.Equal(media.SpoutAdapterLuid, observed.SpoutAdapterLuid);
        Assert.Equal(media.EncoderAdapterLuid, observed.EncoderAdapterLuid);
        Assert.Equal(media.GpuIdentity, observed.GpuIdentity);
        Assert.Equal(2u, observed.SourcePixelFormat);
        Assert.Equal(59.94, observed.EstimatedSourceFramesPerSecond);
        await session.AbortAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DefaultMediaCreateContractUsesDesignDefaults()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            new PendingRecording(
                Path.Combine(directory.Path, "take.recording.mp4"),
                Path.Combine(directory.Path, "take.mp4")),
            new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
            new FrameRate(30));
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());

        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);
        var observed = controls.MediaConfig();

        Assert.Equal(1u, observed.AudioRouting);
        Assert.Equal(2u, observed.QualityPreset);
        Assert.Equal("default-render", observed.DesktopEndpointId);
        Assert.Equal("default-capture", observed.MicrophoneEndpointId);
        Assert.Equal(-6.0, observed.DesktopGainDb);
        Assert.Equal(-6.0, observed.MicrophoneGainDb);
        Assert.Equal("unidentified-spout-sender", observed.SpoutSenderIdentity);
        Assert.Equal(0ul, observed.SpoutAdapterLuid);
        Assert.Equal(0ul, observed.EncoderAdapterLuid);
        Assert.Equal("unidentified-gpu", observed.GpuIdentity);
        await session.AbortAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StableRuntimeLayoutUpdateCrossesManagedNativeAbi()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var signal = new StableVideoSignal(1081, 1921);
        var layoutSession = RecordingVideoLayoutSession.Start(
            signal,
            ResolutionChangePolicy.SingleFileFit);
        var plan = new RecordingPlan(
            signal,
            new PendingRecording(
                Path.Combine(directory.Path, "take.recording.mp4"),
                Path.Combine(directory.Path, "take.mp4")),
            new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
            new FrameRate(30),
            EncoderKind.MediaFoundationSoftware,
            layoutSession);
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);
        var landscape = layoutSession.ApplyStableSignal(
            new StableVideoSignal(1921, 1081));

        await session.UpdateVideoLayoutAsync(
            landscape,
            CancellationToken.None);
        var observed = controls.VideoLayout();

        Assert.Equal(landscape.Source.Width, observed.SourceWidth);
        Assert.Equal(landscape.Source.Height, observed.SourceHeight);
        Assert.Equal(landscape.OutputCanvas.Width, observed.CanvasWidth);
        Assert.Equal(landscape.OutputCanvas.Height, observed.CanvasHeight);
        Assert.Equal(landscape.Placement.OffsetX, observed.DestinationX);
        Assert.Equal(landscape.Placement.OffsetY, observed.DestinationY);
        Assert.Equal(landscape.Placement.Width, observed.DestinationWidth);
        Assert.Equal(landscape.Placement.Height, observed.DestinationHeight);
        Assert.Equal(1u, observed.CanvasBackground);
        Assert.Equal(1u, observed.Rotation);
        await session.AbortAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.UpdateVideoLayoutAsync(
                landscape,
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.GetStatisticsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StatisticsRemainAvailableAfterGracefulStopDestroysHandle()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            new PendingRecording(
                Path.Combine(directory.Path, "take.recording.mp4"),
                Path.Combine(directory.Path, "take.mp4")),
            new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
            new FrameRate(30));
        var expected = new NativeRecordingSessionStatistics(
            SourceVideoFrameCount: 120,
            MuxedVideoPacketCount: 90,
            MuxedAudioPacketCount: 142,
            DroppedSourceVideoFrameCount: 30,
            DuplicatedOutputVideoFrameCount: 4,
            LatestEncodeLatency: TimeSpan.FromMicroseconds(2400),
            MaximumEncodeLatency: TimeSpan.FromMicroseconds(8000),
            AudioVideoOffset: TimeSpan.FromMicroseconds(-15000));
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);
        controls.SetStatistics(expected);

        var active = await session.GetStatisticsAsync(CancellationToken.None);
        var stopping = session.StopAsync(CancellationToken.None);
        controls.CompleteTrailerFlushClose(90, 142);
        await stopping;
        var terminal = await session.GetStatisticsAsync(CancellationToken.None);

        Assert.Equal(expected, active);
        Assert.Equal(expected, terminal);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.UpdateVideoLayoutAsync(
                plan.VideoLayout.CurrentLayout,
                CancellationToken.None));
    }

    [Fact]
    public async Task FinalStatisticsFailureDoesNotReplaceSuccessfulStopResult()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            pending,
            new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
            new FrameRate(30));
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);
        controls.SetStatisticsStatus(status: 6);

        var stopping = session.StopAsync(CancellationToken.None);
        controls.CompleteTrailerFlushClose(90, 142);
        var stopped = await stopping;
        var exception = await Assert.ThrowsAsync<NativeRecordingException>(
            () => session.GetStatisticsAsync(CancellationToken.None));

        Assert.Equal(pending, stopped.Recording);
        Assert.Equal(90, stopped.VideoPacketCount);
        Assert.Equal(142, stopped.AudioPacketCount);
        Assert.Equal(6, exception.Fault.Status);
    }

    [Fact]
    public async Task InvalidNativeStatisticRangeIsReportedWithoutBreakingStop()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            new PendingRecording(
                Path.Combine(directory.Path, "take.recording.mp4"),
                Path.Combine(directory.Path, "take.mp4")),
            new RecordingSessionTimestamp(DateTimeOffset.UnixEpoch),
            new FrameRate(30));
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);
        controls.SetRawStatistics(
            latestEncodeLatencyMicroseconds: ulong.MaxValue,
            maximumEncodeLatencyMicroseconds: ulong.MaxValue,
            audioVideoOffsetMicroseconds: long.MaxValue);

        var activeFailure = await Record.ExceptionAsync(
            () => session.GetStatisticsAsync(CancellationToken.None));
        var stopping = session.StopAsync(CancellationToken.None);
        controls.CompleteTrailerFlushClose(90, 142);
        var stopped = await stopping;
        var terminalException = await Assert.ThrowsAsync<NativeRecordingException>(
            () => session.GetStatisticsAsync(CancellationToken.None));

        var activeException = Assert.IsType<NativeRecordingException>(
            activeFailure);
        Assert.Equal(6, activeException.Fault.Status);
        Assert.Equal(6, terminalException.Fault.Status);
        Assert.Equal(90, stopped.VideoPacketCount);
        Assert.Equal(142, stopped.AudioPacketCount);
    }

    [Theory]
    [InlineData(EncoderKind.Nvenc, 1u)]
    [InlineData(EncoderKind.Amf, 2u)]
    [InlineData(EncoderKind.Qsv, 3u)]
    [InlineData(EncoderKind.MediaFoundationSoftware, 4u)]
    public async Task SelectedEncoderKindCrossesManagedNativeAbi(
        EncoderKind encoder,
        uint expectedNativeEncoder)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            pending,
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(60),
            encoder);
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());

        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);

        Assert.Equal(expectedNativeEncoder, controls.EncoderKind());
        await session.AbortAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisposeIsRejectedWhileNativeSessionIsActive()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            pending,
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(30));
        using var controls = new NativeFixtureControls(FixturePath());
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(() => { }, _ => { }),
            CancellationToken.None);

        var activeDisposeFailure = Record.Exception(backend.Dispose);
        var stopping = session.StopAsync(CancellationToken.None);
        controls.CompleteTrailerFlushClose(
            videoPacketCount: 90,
            audioPacketCount: 142);
        var stopped = await stopping;
        var terminalDisposeFailure = Record.Exception(backend.Dispose);

        var exception = Assert.IsType<InvalidOperationException>(
            activeDisposeFailure);
        Assert.Equal(
            "The native recording backend has an active session.",
            exception.Message);
        Assert.Equal(pending, stopped.Recording);
        Assert.Null(terminalDisposeFailure);
    }

    [Fact]
    public async Task ThrowingManagedFaultCallbackStillFaultsPendingStop()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            pending,
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(30));
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        using var controls = new NativeFixtureControls(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(
                () => { },
                _ => throw new InvalidOperationException(
                    "The managed fault callback failed.")),
            CancellationToken.None);
        var stopping = session.StopAsync(CancellationToken.None);

        controls.Fail(
            status: 6,
            message: "encoder failed while stopping");

        var completed = await Task.WhenAny(
            stopping,
            Task.Delay(TimeSpan.FromMilliseconds(250)));
        if (!ReferenceEquals(stopping, completed))
        {
            await session.AbortAsync(CancellationToken.None);
        }

        Assert.Same(stopping, completed);
        var exception = await Assert.ThrowsAsync<NativeRecordingException>(
            () => stopping);
        Assert.Equal(6, exception.Fault.Status);
        Assert.Equal("encoder failed while stopping", exception.Fault.Message);
    }

    [Fact]
    public async Task StopCompletesOnlyAfterNativeStoppedEventWithPacketCounts()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            pending,
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(30));
        var firstPacket = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedFault = new TaskCompletionSource<NativeRecordingFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        using var controls = new NativeFixtureControls(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(
                () => firstPacket.TrySetResult(),
                fault => unexpectedFault.TrySetResult(fault)),
            CancellationToken.None);

        Assert.False(firstPacket.Task.IsCompleted);
        controls.CommitMuxedVideoPacket();
        await firstPacket.Task;

        var stopping = session.StopAsync(CancellationToken.None);
        Assert.False(stopping.IsCompleted);
        controls.CompleteTrailerFlushClose(
            videoPacketCount: 90,
            audioPacketCount: 142);
        var stopped = await stopping;

        Assert.Equal(pending, stopped.Recording);
        Assert.Equal(90, stopped.VideoPacketCount);
        Assert.Equal(142, stopped.AudioPacketCount);
        Assert.False(unexpectedFault.Task.IsCompleted);
    }

    private static string FixturePath()
    {
        var root = FindRepositoryRoot();
        return Path.Combine(
            root,
            "tests",
            "VRRecorder.Native.Tests",
            "build",
            "libvrrecorder_native_test.so");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VR-Recorder.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "The VR-Recorder repository root was not found.");
    }

    private sealed class NativeFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly CommitDelegate _commit;
        private readonly CompleteDelegate _complete;
        private readonly FailDelegate _fail;
        private readonly EncoderKindDelegate _encoderKind;
        private readonly CopyMediaConfigDelegate _copyMediaConfig;
        private readonly CopyVideoLayoutDelegate _copyVideoLayout;
        private readonly SetStatisticsDelegate _setStatistics;
        private readonly SetStatisticsStatusDelegate _setStatisticsStatus;

        public NativeFixtureControls(string path)
        {
            _library = NativeLibrary.Load(path);
            _commit = Marshal.GetDelegateForFunctionPointer<CommitDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_commit_muxed_video_packet"));
            _complete = Marshal.GetDelegateForFunctionPointer<CompleteDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_complete_trailer_flush_close"));
            _fail = Marshal.GetDelegateForFunctionPointer<FailDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_fail"));
            _encoderKind = Marshal.GetDelegateForFunctionPointer<EncoderKindDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_encoder_kind"));
            _copyMediaConfig = Marshal.GetDelegateForFunctionPointer<
                CopyMediaConfigDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_copy_media_config_v1"));
            _copyVideoLayout = Marshal.GetDelegateForFunctionPointer<
                CopyVideoLayoutDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_copy_video_layout_v1"));
            _setStatistics = Marshal.GetDelegateForFunctionPointer<
                SetStatisticsDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_set_statistics_v1"));
            _setStatisticsStatus = Marshal.GetDelegateForFunctionPointer<
                SetStatisticsStatusDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_set_statistics_status"));
        }

        public void CommitMuxedVideoPacket() => _commit();

        public void CompleteTrailerFlushClose(
            ulong videoPacketCount,
            ulong audioPacketCount) =>
            _complete(videoPacketCount, audioPacketCount);

        public void Fail(int status, string message) => _fail(status, message);

        public uint EncoderKind() => _encoderKind();

        public ObservedMediaConfig MediaConfig()
        {
            var native = new NativeObservedMediaConfig();
            _copyMediaConfig(ref native);
            return new ObservedMediaConfig(
                native.CanvasWidth,
                native.CanvasHeight,
                native.SourceWidth,
                native.SourceHeight,
                native.DestinationX,
                native.DestinationY,
                native.DestinationWidth,
                native.DestinationHeight,
                native.CanvasBackground,
                native.Rotation,
                native.AudioRouting,
                native.QualityPreset,
                Marshal.PtrToStringUTF8(native.DesktopEndpointIdUtf8)!,
                Marshal.PtrToStringUTF8(native.MicrophoneEndpointIdUtf8)!,
                native.DesktopGainDb,
                native.MicrophoneGainDb,
                Marshal.PtrToStringUTF8(native.SpoutSenderIdentityUtf8)!,
                native.SpoutAdapterLuid,
                native.EncoderAdapterLuid,
                Marshal.PtrToStringUTF8(native.GpuIdentityUtf8)!);
        }

        public ObservedVideoLayout VideoLayout()
        {
            var native = new NativeObservedVideoLayout();
            _copyVideoLayout(ref native);
            return new ObservedVideoLayout(
                checked((int)native.SourceWidth),
                checked((int)native.SourceHeight),
                checked((int)native.CanvasWidth),
                checked((int)native.CanvasHeight),
                checked((int)native.DestinationX),
                checked((int)native.DestinationY),
                checked((int)native.DestinationWidth),
                checked((int)native.DestinationHeight),
                native.CanvasBackground,
                native.Rotation);
        }

        public void SetStatistics(NativeRecordingSessionStatistics statistics) =>
            _setStatistics(
                statistics.SourceVideoFrameCount,
                statistics.MuxedVideoPacketCount,
                statistics.MuxedAudioPacketCount,
                statistics.DroppedSourceVideoFrameCount,
                statistics.DuplicatedOutputVideoFrameCount,
                checked((ulong)statistics.LatestEncodeLatency.TotalMicroseconds),
                checked((ulong)statistics.MaximumEncodeLatency.TotalMicroseconds),
                checked((long)statistics.AudioVideoOffset.TotalMicroseconds));

        public void SetStatisticsStatus(int status) =>
            _setStatisticsStatus(status);

        public void SetRawStatistics(
            ulong latestEncodeLatencyMicroseconds,
            ulong maximumEncodeLatencyMicroseconds,
            long audioVideoOffsetMicroseconds) =>
            _setStatistics(
                sourceVideoFrameCount: 120,
                muxedVideoPacketCount: 90,
                muxedAudioPacketCount: 142,
                droppedSourceVideoFrameCount: 30,
                duplicatedOutputVideoFrameCount: 4,
                latestEncodeLatencyMicroseconds,
                maximumEncodeLatencyMicroseconds,
                audioVideoOffsetMicroseconds);

        public void Dispose() => NativeLibrary.Free(_library);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CommitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CompleteDelegate(
            ulong videoPacketCount,
            ulong audioPacketCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FailDelegate(
            int status,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint EncoderKindDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CopyMediaConfigDelegate(
            ref NativeObservedMediaConfig config);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CopyVideoLayoutDelegate(
            ref NativeObservedVideoLayout layout);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetStatisticsDelegate(
            ulong sourceVideoFrameCount,
            ulong muxedVideoPacketCount,
            ulong muxedAudioPacketCount,
            ulong droppedSourceVideoFrameCount,
            ulong duplicatedOutputVideoFrameCount,
            ulong latestEncodeLatencyMicroseconds,
            ulong maximumEncodeLatencyMicroseconds,
            long audioVideoOffsetMicroseconds);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetStatisticsStatusDelegate(int status);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeObservedMediaConfig
        {
            public uint CanvasWidth;
            public uint CanvasHeight;
            public uint SourceWidth;
            public uint SourceHeight;
            public uint DestinationX;
            public uint DestinationY;
            public uint DestinationWidth;
            public uint DestinationHeight;
            public uint CanvasBackground;
            public uint Rotation;
            public uint AudioRouting;
            public uint QualityPreset;
            public nint DesktopEndpointIdUtf8;
            public nint MicrophoneEndpointIdUtf8;
            public double DesktopGainDb;
            public double MicrophoneGainDb;
            public nint SpoutSenderIdentityUtf8;
            public ulong SpoutAdapterLuid;
            public ulong EncoderAdapterLuid;
            public nint GpuIdentityUtf8;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeObservedVideoLayout
        {
            public uint SourceWidth;
            public uint SourceHeight;
            public uint CanvasWidth;
            public uint CanvasHeight;
            public uint DestinationX;
            public uint DestinationY;
            public uint DestinationWidth;
            public uint DestinationHeight;
            public uint CanvasBackground;
            public uint Rotation;
        }
    }

    private sealed record ObservedMediaConfig(
        uint CanvasWidth,
        uint CanvasHeight,
        uint SourceWidth,
        uint SourceHeight,
        uint DestinationX,
        uint DestinationY,
        uint DestinationWidth,
        uint DestinationHeight,
        uint CanvasBackground,
        uint Rotation,
        uint AudioRouting,
        uint QualityPreset,
        string DesktopEndpointId,
        string MicrophoneEndpointId,
        double DesktopGainDb,
        double MicrophoneGainDb,
        string SpoutSenderIdentity,
        ulong SpoutAdapterLuid,
        ulong EncoderAdapterLuid,
        string GpuIdentity);

    private sealed record ObservedVideoLayout(
        int SourceWidth,
        int SourceHeight,
        int CanvasWidth,
        int CanvasHeight,
        int DestinationX,
        int DestinationY,
        int DestinationWidth,
        int DestinationHeight,
        uint CanvasBackground,
        uint Rotation);

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
                $"vr-recorder-pinvoke-tests-{Guid.NewGuid():N}");
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
