using System.Runtime.InteropServices;
using VRRecorder.Application.Encoding;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class PInvokeEncoderProbeTests
{
    [Fact]
    public async Task ProducedPacketCrossesTheExactNativeProbeRequest()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeEncoderProbeFixtureControls(FixturePath());
        controls.Reset();
        controls.SetResult(status: 0, packetProduced: true);
        using var probe = new PInvokeEncoderProbe(FixturePath());
        var request = new EncoderProbeRequest(
            EncoderKind.Nvenc,
            0x00000001ABCDEF01,
            "pci\\ven_10de&dev_2684|driver-32.0.15.6094",
            1920,
            1080,
            new FrameRate(60));

        var result = await probe.ProbeAsync(
            request,
            CancellationToken.None);

        Assert.Equal(EncoderProbeResult.PacketProduced, result);
        Assert.Equal(1u, controls.CallCount());
        var observed = controls.ReadConfig();
        Assert.Equal(1u, observed.EncoderKind);
        Assert.Equal(16u, observed.SyntheticFrameCount);
        Assert.Equal(request.AdapterLuid, observed.AdapterLuid);
        Assert.Equal((uint)request.Width, observed.Width);
        Assert.Equal((uint)request.Height, observed.Height);
        Assert.Equal((uint)request.FrameRate.Value, observed.FramesPerSecondNumerator);
        Assert.Equal(1u, observed.FramesPerSecondDenominator);
        Assert.Equal(request.GpuIdentity, observed.GpuIdentity);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    public async Task NoPacketOrBackendProbeFailureMapsToFallback(
        int nativeStatus,
        bool packetProduced)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeEncoderProbeFixtureControls(FixturePath());
        controls.Reset();
        controls.SetResult(nativeStatus, packetProduced);
        using var probe = new PInvokeEncoderProbe(FixturePath());

        var result = await probe.ProbeAsync(
            Request(),
            CancellationToken.None);

        Assert.Equal(EncoderProbeResult.Failed, result);
        Assert.Equal(1u, controls.CallCount());
    }

    [Fact]
    public async Task CancellationBeforeDispatchNeverCallsNativeProbe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeEncoderProbeFixtureControls(FixturePath());
        controls.Reset();
        using var probe = new PInvokeEncoderProbe(FixturePath());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            probe.ProbeAsync(Request(), cancellation.Token));

        Assert.Equal(0u, controls.CallCount());
    }

    [Fact]
    public async Task DisposeWaitsForInFlightProbeAndSuppressesLateResult()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeEncoderProbeFixtureControls(FixturePath());
        controls.Reset();
        controls.SetResult(status: 0, packetProduced: true);
        controls.BlockNextProbe();
        var probe = new PInvokeEncoderProbe(FixturePath());

        var probing = probe.ProbeAsync(Request(), CancellationToken.None);
        await controls.WaitUntilProbeEnteredAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));
        var disposeAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposal = Task.Run(() =>
        {
            disposeAttempted.TrySetResult();
            probe.Dispose();
        });
        await disposeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(disposal.IsCompleted);
        controls.ReleaseProbe();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => probing)
            .WaitAsync(TimeSpan.FromSeconds(1));
        await disposal.WaitAsync(TimeSpan.FromSeconds(1));
        probe.Dispose();
        Assert.Equal(1u, controls.CallCount());
    }

    private static EncoderProbeRequest Request() => new(
        EncoderKind.MediaFoundationSoftware,
        adapterLuid: 1,
        "software-probe-adapter",
        1280,
        720,
        new FrameRate(30));

    private static string FixturePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "VRRecorder.Native.Tests",
        "build",
        "libvrrecorder_native_test.so"));

    private sealed class NativeEncoderProbeFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly ResetDelegate _reset;
        private readonly SetResultDelegate _setResult;
        private readonly CountDelegate _callCount;
        private readonly CopyConfigDelegate _copyConfig;
        private readonly VoidDelegate _blockNextProbe;
        private readonly WaitDelegate _waitUntilProbeEntered;
        private readonly VoidDelegate _releaseProbe;

        public NativeEncoderProbeFixtureControls(string path)
        {
            _library = NativeLibrary.Load(path);
            _reset = Resolve<ResetDelegate>("vrrec_test_encoder_probe_reset");
            _setResult = Resolve<SetResultDelegate>(
                "vrrec_test_encoder_probe_set_result");
            _callCount = Resolve<CountDelegate>(
                "vrrec_test_encoder_probe_call_count");
            _copyConfig = Resolve<CopyConfigDelegate>(
                "vrrec_test_encoder_probe_copy_config_v1");
            _blockNextProbe = Resolve<VoidDelegate>(
                "vrrec_test_encoder_probe_block_next");
            _waitUntilProbeEntered = Resolve<WaitDelegate>(
                "vrrec_test_encoder_probe_wait_until_entered");
            _releaseProbe = Resolve<VoidDelegate>(
                "vrrec_test_encoder_probe_release");
        }

        public void Reset() => _reset();

        public void SetResult(int status, bool packetProduced) =>
            _setResult(status, packetProduced ? (byte)1 : (byte)0);

        public uint CallCount() => _callCount();

        public void BlockNextProbe() => _blockNextProbe();

        public async Task WaitUntilProbeEnteredAsync()
        {
            var entered = await Task.Run(() =>
                _waitUntilProbeEntered(milliseconds: 1_000));
            Assert.Equal(1, entered);
        }

        public void ReleaseProbe() => _releaseProbe();

        public ObservedConfig ReadConfig()
        {
            _copyConfig(out var native);
            return new ObservedConfig(
                native.EncoderKind,
                native.SyntheticFrameCount,
                native.AdapterLuid,
                native.Width,
                native.Height,
                native.FramesPerSecondNumerator,
                native.FramesPerSecondDenominator,
                Marshal.PtrToStringUTF8(native.GpuIdentityUtf8) ?? string.Empty);
        }

        public void Dispose() => NativeLibrary.Free(_library);

        private TDelegate Resolve<TDelegate>(string exportName)
            where TDelegate : Delegate =>
            Marshal.GetDelegateForFunctionPointer<TDelegate>(
                NativeLibrary.GetExport(_library, exportName));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ResetDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetResultDelegate(int status, byte packetProduced);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint CountDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CopyConfigDelegate(out NativeObservedConfig config);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int WaitDelegate(uint milliseconds);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeObservedConfig
        {
            public uint EncoderKind;
            public uint SyntheticFrameCount;
            public ulong AdapterLuid;
            public uint Width;
            public uint Height;
            public uint FramesPerSecondNumerator;
            public uint FramesPerSecondDenominator;
            public nint GpuIdentityUtf8;
        }
    }

    private sealed record ObservedConfig(
        uint EncoderKind,
        uint SyntheticFrameCount,
        ulong AdapterLuid,
        uint Width,
        uint Height,
        uint FramesPerSecondNumerator,
        uint FramesPerSecondDenominator,
        string GpuIdentity);
}
