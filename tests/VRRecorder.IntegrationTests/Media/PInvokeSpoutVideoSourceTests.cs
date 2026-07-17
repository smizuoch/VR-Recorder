using System.Runtime.InteropServices;
using System.Text;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;
using VRRecorder.Infrastructure.Media.Native;

namespace VRRecorder.IntegrationTests.Media;

public sealed class PInvokeSpoutVideoSourceTests
{
    [Fact]
    public async Task SnapshotAndFramesFeedTheExistingStabilityGateway()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        const ulong adapterLuid = 0x00000001ABCDEF01;
        var longSenderId = $"VRChat-Spout-{new string('送', 300)}";
        var longGpuIdentity = $"GPU-{new string('界', 300)}";
        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        controls.AddSnapshotSender("pre-start", 90);
        controls.AddSnapshotSender(longSenderId, 41);
        using var source = new PInvokeSpoutVideoSource(
            FixturePath(),
            TimeSpan.FromMilliseconds(10));
        var gateway = new SpoutVideoSignalGateway(source);

        await gateway.CaptureBaselineAsync(CancellationToken.None);
        var stable = gateway.WaitForStableSignalAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        controls.PushFrame(
            longSenderId,
            adapterLuid,
            longGpuIdentity,
            GpuVendor.Nvidia,
            1920,
            1080,
            VideoPixelFormat.Rgba8,
            59.8,
            42,
            1_000_000);
        controls.PushFrame(
            longSenderId,
            adapterLuid,
            longGpuIdentity,
            GpuVendor.Nvidia,
            1920,
            1080,
            VideoPixelFormat.Rgba8,
            60.1,
            43,
            1_150_000);
        controls.PushFrame(
            longSenderId,
            adapterLuid,
            longGpuIdentity,
            GpuVendor.Nvidia,
            1920,
            1080,
            VideoPixelFormat.Rgba8,
            59.94,
            44,
            1_300_000);

        var signal = await stable;

        Assert.Equal(longSenderId, signal.SenderId);
        Assert.Equal(adapterLuid, signal.AdapterLuid);
        Assert.Equal(longGpuIdentity, signal.GpuIdentity);
        Assert.Equal(GpuVendor.Nvidia, signal.GpuVendor);
        Assert.Equal(1920, signal.Width);
        Assert.Equal(1080, signal.Height);
        Assert.Equal(VideoPixelFormat.Rgba8, signal.PixelFormat);
        Assert.Equal(59.94, signal.EstimatedSourceFramesPerSecond);
        Assert.Equal(1u, controls.ActiveSourceCount());
    }

    [Fact]
    public async Task CallerCancellationInterruptsEmptyPollingWithinOneSlice()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        using var source = new PInvokeSpoutVideoSource(
            FixturePath(),
            TimeSpan.FromMilliseconds(10));
        using var cancellation = new CancellationTokenSource();
        await using var frames = source
            .ObserveFramesAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var pending = frames.MoveNextAsync().AsTask();
        await controls.WaitUntilPollEnteredAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending)
            .WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DisposeJoinsBlockedPollAndDestroysTheNativeHandleOnce()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        var source = new PInvokeSpoutVideoSource(
            FixturePath(),
            TimeSpan.FromMilliseconds(10));
        controls.BlockNextPoll();
        await using var frames = source
            .ObserveFramesAsync(CancellationToken.None)
            .GetAsyncEnumerator();
        var pending = frames.MoveNextAsync().AsTask();
        await controls.WaitUntilPollEnteredAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        var disposal = Task.Run(source.Dispose);
        await Task.Delay(TimeSpan.FromMilliseconds(25));
        Assert.False(disposal.IsCompleted);
        var repeatedDisposal = Task.Run(source.Dispose);
        await Task.Delay(TimeSpan.FromMilliseconds(25));
        Assert.False(repeatedDisposal.IsCompleted);

        controls.ReleasePoll();
        await disposal.WaitAsync(TimeSpan.FromSeconds(1));
        await repeatedDisposal.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending)
            .WaitAsync(TimeSpan.FromSeconds(1));
        source.Dispose();

        Assert.Equal(0u, controls.ActiveSourceCount());
        Assert.Equal(1u, controls.DestroyCount());
    }

    [Fact]
    public async Task DisposingOneSourceDoesNotInvalidateAnotherLibraryLease()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        controls.AddSnapshotSender("shared-library-sender", 7);
        var first = new PInvokeSpoutVideoSource(FixturePath());
        using var second = new PInvokeSpoutVideoSource(FixturePath());
        Assert.Equal(2u, controls.ActiveSourceCount());

        first.Dispose();
        var snapshot = await second.SnapshotAsync(CancellationToken.None);

        var sender = Assert.Single(snapshot);
        Assert.Equal("shared-library-sender", sender.SenderId);
        Assert.Equal(7ul, sender.LatestFrameSequence);
        Assert.Equal(1u, controls.ActiveSourceCount());
    }

    [Fact]
    public async Task EmptySnapshotAndDisposedUseAreDeterministic()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        var source = new PInvokeSpoutVideoSource(FixturePath());

        Assert.Empty(await source.SnapshotAsync(CancellationToken.None));

        source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            source.SnapshotAsync(CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.SnapshotAsync(cancellation.Token));
    }

    [Fact]
    public void RejectsPollSliceOutsideNativeContract()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PInvokeSpoutVideoSource(FixturePath(), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PInvokeSpoutVideoSource(
                FixturePath(),
                TimeSpan.FromMilliseconds(1_001)));
    }

    [Fact]
    public void SnapshotDecoderAcceptsPackedSenders()
    {
        var first = Encoding.UTF8.GetBytes("sender-a");
        var second = Encoding.UTF8.GetBytes("sender-b");
        var utf8 = first.Concat(second).ToArray();
        var entries = PInvokeSpoutVideoSource.CreateSnapshotEntries(2);
        entries[0].SenderIdOffset = 0;
        entries[0].SenderIdSize = checked((uint)first.Length);
        entries[0].LatestFrameGeneration = 10;
        entries[1].SenderIdOffset = checked((uint)first.Length);
        entries[1].SenderIdSize = checked((uint)second.Length);
        entries[1].LatestFrameGeneration = 20;

        var decoded = PInvokeSpoutVideoSource.DecodeSnapshot(
            entries,
            utf8,
            entryCount: 2);

        Assert.Collection(
            decoded,
            sender =>
            {
                Assert.Equal("sender-a", sender.SenderId);
                Assert.Equal(10ul, sender.LatestFrameSequence);
            },
            sender =>
            {
                Assert.Equal("sender-b", sender.SenderId);
                Assert.Equal(20ul, sender.LatestFrameSequence);
            });
    }

    [Fact]
    public void FrameDecoderAcceptsEveryGpuVendorAndPixelFormat()
    {
        var sender = Encoding.UTF8.GetBytes("sender");
        var gpu = Encoding.UTF8.GetBytes("gpu-identity");
        var utf8 = sender.Concat(gpu).ToArray();
        foreach (var vendor in Enum.GetValues<NativeGpuVendor>())
        {
            foreach (var pixelFormat in Enum.GetValues<NativeSourcePixelFormat>())
            {
                var frame = PInvokeSpoutVideoSource.CreateFrameOutput();
                frame.SenderIdOffset = 0;
                frame.SenderIdSize = checked((uint)sender.Length);
                frame.GpuIdentityOffset = checked((uint)sender.Length);
                frame.GpuIdentitySize = checked((uint)gpu.Length);
                frame.AdapterLuid = 42;
                frame.GpuVendor = vendor;
                frame.Width = 1920;
                frame.Height = 1080;
                frame.PixelFormat = pixelFormat;
                frame.EstimatedSourceFramesPerSecond = 59.94;
                frame.FrameSequence = 7;
                frame.MonotonicTimestampMicroseconds = 1_000;

                var decoded = PInvokeSpoutVideoSource.DecodeFrame(frame, utf8);

                Assert.Equal("sender", decoded.Signal.SenderId);
                Assert.Equal("gpu-identity", decoded.Signal.GpuIdentity);
                Assert.Equal(7ul, decoded.FrameSequence);
            }
        }
    }

    [Fact]
    public void FrameDecoderRejectsReservedAndUnknownNativeValues()
    {
        var utf8 = Encoding.UTF8.GetBytes("sendergpu");
        var frame = PInvokeSpoutVideoSource.CreateFrameOutput();
        frame.SenderIdSize = 6;
        frame.GpuIdentityOffset = 6;
        frame.GpuIdentitySize = 3;
        frame.GpuVendor = NativeGpuVendor.Nvidia;
        frame.Width = 1;
        frame.Height = 1;
        frame.PixelFormat = NativeSourcePixelFormat.Bgra8;
        frame.EstimatedSourceFramesPerSecond = 1;
        frame.Reserved = 1;
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.DecodeFrame(frame, utf8));

        frame.Reserved = 0;
        frame.GpuVendor = (NativeGpuVendor)999;
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.DecodeFrame(frame, utf8));
        frame.GpuVendor = NativeGpuVendor.Nvidia;
        frame.PixelFormat = (NativeSourcePixelFormat)999;
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.DecodeFrame(frame, utf8));
    }

    [Theory]
    [InlineData(SpoutTextFailure.ZeroSize)]
    [InlineData(SpoutTextFailure.RangeBeyondBuffer)]
    [InlineData(SpoutTextFailure.InvalidUtf8)]
    [InlineData(SpoutTextFailure.Whitespace)]
    [InlineData(SpoutTextFailure.ControlCharacter)]
    public void Utf8DecoderRejectsInvalidNativeText(SpoutTextFailure failure)
    {
        var bytes = failure switch
        {
            SpoutTextFailure.InvalidUtf8 => new byte[] { 0xff },
            SpoutTextFailure.Whitespace => " "u8.ToArray(),
            SpoutTextFailure.ControlCharacter => "a\n"u8.ToArray(),
            _ => "x"u8.ToArray(),
        };
        var size = failure == SpoutTextFailure.ZeroSize
            ? 0u
            : checked((uint)bytes.Length);
        var offset = failure == SpoutTextFailure.RangeBeyondBuffer
            ? checked((uint)bytes.Length)
            : 0u;

        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.DecodeUtf8(
                bytes,
                offset,
                size,
                "test field"));
    }

    [Fact]
    public void NativeLayoutAndCapacityValidatorsFailClosed()
    {
        PInvokeSpoutVideoSource.ValidateOutputHeader(
            structSize: 24,
            abiVersion: NativeSpoutSourceLibrary.SupportedAbiVersion,
            expectedSize: 24,
            "valid");
        PInvokeSpoutVideoSource.ValidateRequiredSizes(1, 1, "valid");
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.ValidateOutputHeader(
                structSize: 23,
                abiVersion: NativeSpoutSourceLibrary.SupportedAbiVersion,
                expectedSize: 24,
                "small"));
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.ValidateOutputHeader(
                structSize: 24,
                abiVersion: 999,
                expectedSize: 24,
                "ABI"));
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.ValidateRequiredSizes(
                NativeSpoutSourceLibrary.MaximumSnapshotEntries + 1,
                1,
                "entries"));
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.ValidateRequiredSizes(
                1,
                NativeSpoutSourceLibrary.MaximumUtf8BufferSize + 1,
                "UTF-8"));
    }

    [Fact]
    public void NativeMappingsCoverEveryValueAndRejectUnknowns()
    {
        Assert.Equal(
            GpuVendor.Unknown,
            PInvokeSpoutVideoSource.ConvertGpuVendor(NativeGpuVendor.Unknown));
        Assert.Equal(
            GpuVendor.Nvidia,
            PInvokeSpoutVideoSource.ConvertGpuVendor(NativeGpuVendor.Nvidia));
        Assert.Equal(
            GpuVendor.Amd,
            PInvokeSpoutVideoSource.ConvertGpuVendor(NativeGpuVendor.Amd));
        Assert.Equal(
            GpuVendor.Intel,
            PInvokeSpoutVideoSource.ConvertGpuVendor(NativeGpuVendor.Intel));
        Assert.Equal(
            VideoPixelFormat.Bgra8,
            PInvokeSpoutVideoSource.ConvertPixelFormat(
                NativeSourcePixelFormat.Bgra8));
        Assert.Equal(
            VideoPixelFormat.Rgba8,
            PInvokeSpoutVideoSource.ConvertPixelFormat(
                NativeSourcePixelFormat.Rgba8));
        Assert.Equal(
            VideoPixelFormat.Nv12,
            PInvokeSpoutVideoSource.ConvertPixelFormat(
                NativeSourcePixelFormat.Nv12));
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.ConvertGpuVendor((NativeGpuVendor)999));
        Assert.Throws<NativeSpoutSourceException>(() =>
            PInvokeSpoutVideoSource.ConvertPixelFormat(
                (NativeSourcePixelFormat)999));
    }

    [Fact]
    public void NativeCreateFailureIsTypedAndDoesNotLeakSource()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        controls.FailNextCreate(status: 4);

        var exception = Assert.Throws<NativeSpoutSourceException>(() =>
            new PInvokeSpoutVideoSource(FixturePath()));

        Assert.Equal(4, exception.Status);
        Assert.Contains("create failed", exception.Message);
        Assert.Equal(0u, controls.ActiveSourceCount());
    }

    [Fact]
    public async Task NativeSnapshotFailureIsTypedAndSourceRemainsDisposable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        using var source = new PInvokeSpoutVideoSource(FixturePath());
        controls.FailNextSnapshot(status: 6);

        var exception = await Assert.ThrowsAsync<NativeSpoutSourceException>(() =>
            source.SnapshotAsync(CancellationToken.None));

        Assert.Equal(6, exception.Status);
        Assert.Contains("snapshot failed", exception.Message);
    }

    [Theory]
    [InlineData(6, "frame size query")]
    [InlineData(7, "frame read")]
    public async Task NativePollFailuresAreTyped(
        int status,
        string expectedOperation)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeSpoutFixtureControls(FixturePath());
        controls.Reset();
        using var source = new PInvokeSpoutVideoSource(FixturePath());
        controls.FailNextPoll(status);
        await using var frames = source
            .ObserveFramesAsync(CancellationToken.None)
            .GetAsyncEnumerator();

        var exception = await Assert.ThrowsAsync<NativeSpoutSourceException>(
            () => frames.MoveNextAsync().AsTask());

        Assert.Contains(expectedOperation, exception.Message);
    }

    public enum SpoutTextFailure
    {
        ZeroSize,
        RangeBeyondBuffer,
        InvalidUtf8,
        Whitespace,
        ControlCharacter,
    }

    private static string FixturePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "VRRecorder.Native.Tests",
        "build",
        "libvrrecorder_native_test.so"));

    private sealed class NativeSpoutFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly ResetDelegate _reset;
        private readonly AddSnapshotSenderDelegate _addSnapshotSender;
        private readonly PushFrameDelegate _pushFrame;
        private readonly VoidDelegate _blockNextPoll;
        private readonly WaitUntilPollEnteredDelegate _waitUntilPollEntered;
        private readonly VoidDelegate _releasePoll;
        private readonly CountDelegate _activeSourceCount;
        private readonly CountDelegate _destroyCount;
        private readonly StatusDelegate _failNextCreate;
        private readonly StatusDelegate _failNextSnapshot;
        private readonly StatusDelegate _failNextPoll;

        public NativeSpoutFixtureControls(string path)
        {
            _library = NativeLibrary.Load(path);
            _reset = Resolve<ResetDelegate>("vrrec_test_spout_reset");
            _addSnapshotSender = Resolve<AddSnapshotSenderDelegate>(
                "vrrec_test_spout_add_snapshot_sender");
            _pushFrame = Resolve<PushFrameDelegate>(
                "vrrec_test_spout_push_frame");
            _blockNextPoll = Resolve<VoidDelegate>(
                "vrrec_test_spout_block_next_poll");
            _waitUntilPollEntered = Resolve<WaitUntilPollEnteredDelegate>(
                "vrrec_test_spout_wait_until_poll_entered");
            _releasePoll = Resolve<VoidDelegate>(
                "vrrec_test_spout_release_poll");
            _activeSourceCount = Resolve<CountDelegate>(
                "vrrec_test_spout_active_source_count");
            _destroyCount = Resolve<CountDelegate>(
                "vrrec_test_spout_destroy_count");
            _failNextCreate = Resolve<StatusDelegate>(
                "vrrec_test_spout_fail_next_create");
            _failNextSnapshot = Resolve<StatusDelegate>(
                "vrrec_test_spout_fail_next_snapshot");
            _failNextPoll = Resolve<StatusDelegate>(
                "vrrec_test_spout_fail_next_poll");
        }

        public void Reset() => _reset();

        public void AddSnapshotSender(string senderId, ulong generation) =>
            WithUtf8(senderId, pointer =>
                _addSnapshotSender(pointer, generation));

        public void PushFrame(
            string senderId,
            ulong adapterLuid,
            string gpuIdentity,
            GpuVendor gpuVendor,
            uint width,
            uint height,
            VideoPixelFormat pixelFormat,
            double estimatedSourceFramesPerSecond,
            ulong sequence,
            long monotonicTimestampMicroseconds) =>
            WithUtf8(senderId, senderPointer =>
                WithUtf8(gpuIdentity, gpuPointer =>
                    _pushFrame(
                        senderPointer,
                        adapterLuid,
                        gpuPointer,
                        checked((uint)gpuVendor),
                        width,
                        height,
                        checked((uint)pixelFormat + 1),
                        estimatedSourceFramesPerSecond,
                        sequence,
                        monotonicTimestampMicroseconds)));

        public void BlockNextPoll() => _blockNextPoll();

        public async Task WaitUntilPollEnteredAsync()
        {
            var entered = await Task.Run(() =>
                _waitUntilPollEntered(milliseconds: 1_000));
            Assert.Equal(1, entered);
        }

        public void ReleasePoll() => _releasePoll();

        public void FailNextCreate(int status) => _failNextCreate(status);

        public void FailNextSnapshot(int status) => _failNextSnapshot(status);

        public void FailNextPoll(int status) => _failNextPoll(status);

        public uint ActiveSourceCount() => _activeSourceCount();

        public uint DestroyCount() => _destroyCount();

        public void Dispose() => NativeLibrary.Free(_library);

        private TDelegate Resolve<TDelegate>(string exportName)
            where TDelegate : Delegate =>
            Marshal.GetDelegateForFunctionPointer<TDelegate>(
                NativeLibrary.GetExport(_library, exportName));

        private static void WithUtf8(string value, Action<nint> action)
        {
            var pointer = Marshal.StringToCoTaskMemUTF8(value);
            try
            {
                action(pointer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ResetDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AddSnapshotSenderDelegate(
            nint senderIdUtf8,
            ulong generation);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PushFrameDelegate(
            nint senderIdUtf8,
            ulong adapterLuid,
            nint gpuIdentityUtf8,
            uint gpuVendor,
            uint width,
            uint height,
            uint pixelFormat,
            double estimatedSourceFramesPerSecond,
            ulong sequence,
            long monotonicTimestampMicroseconds);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int WaitUntilPollEnteredDelegate(uint milliseconds);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint CountDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StatusDelegate(int status);
    }
}
