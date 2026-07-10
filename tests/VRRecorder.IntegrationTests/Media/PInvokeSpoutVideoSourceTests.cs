using System.Runtime.InteropServices;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

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

        controls.ReleasePoll();
        await disposal.WaitAsync(TimeSpan.FromSeconds(1));
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
    }
}
