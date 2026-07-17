using System.Runtime.InteropServices;
using System.Text;
using VRRecorder.Application.Encoding;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;
using VRRecorder.Infrastructure.Media.Native;

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
        controls.SetVerifiedNvencEvidence(request);

        var result = await probe.ProbeAsync(
            request,
            CancellationToken.None);

        Assert.True(result.IsPacketProduced);
        var evidence = Assert.IsType<EncoderProbeEvidence>(result.Evidence);
        Assert.Equal(EncoderKind.Nvenc, evidence.ActualEncoder);
        Assert.Equal("h264_nvenc", evidence.CodecName);
        Assert.True(evidence.HardwareAccelerated);
        Assert.Equal(request.AdapterLuid, evidence.AdapterLuid);
        Assert.Equal(EncoderInputFormat.D3d11Nv12, evidence.InputFormat);
        Assert.Equal(request.Width, evidence.Width);
        Assert.Equal(request.Height, evidence.Height);
        Assert.Equal(request.FrameRate, evidence.FrameRate);
        Assert.Equal(
            EncoderProbeValidation.CompleteHardwarePacket,
            evidence.Validation);
        Assert.Equal("nvidia|32.0.16.1062", evidence.DriverIdentity);
        Assert.Equal("ffmpeg|8.1.2|contract-id", evidence.FfmpegBuildIdentity);
        Assert.Equal("high", evidence.Profile);
        Assert.Equal("pci\\ven_10de&dev_2684", evidence.DeviceIdentity);
        Assert.Equal(2u, controls.CallCount());
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
    [InlineData(8, true)]
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
        controls.SetVerifiedSoftwareEvidence(Request());
        controls.BlockNextProbe();
        var probe = new PInvokeEncoderProbe(FixturePath());

        var probing = probe.ProbeAsync(Request(), CancellationToken.None);
        await controls.WaitUntilProbeEnteredAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));
        var disposal = probe.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        controls.ReleaseProbe();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => probing)
            .WaitAsync(TimeSpan.FromSeconds(1));
        await disposal.WaitAsync(TimeSpan.FromSeconds(1));
        probe.Dispose();
        Assert.Equal(1u, controls.CallCount());
    }

    [Fact]
    public void EvidenceParserAcceptsEveryEncoderContract()
    {
        foreach (var encoder in Enum.GetValues<EncoderKind>())
        {
            using var fixture = new NativeEvidenceFixture(encoder);

            var evidence = PInvokeEncoderProbe.ReadEvidence(
                fixture.Request,
                fixture.Native,
                fixture.Buffer,
                fixture.BufferSize);

            Assert.Equal(encoder, evidence.ActualEncoder);
            Assert.Equal(
                encoder != EncoderKind.MediaFoundationSoftware,
                evidence.HardwareAccelerated);
            Assert.Equal(fixture.ExpectedCodec, evidence.CodecName);
        }
    }

    [Theory]
    [InlineData(EvidenceMismatch.StructSize)]
    [InlineData(EvidenceMismatch.AbiVersion)]
    [InlineData(EvidenceMismatch.Reserved)]
    [InlineData(EvidenceMismatch.ActualEncoder)]
    [InlineData(EvidenceMismatch.InvalidActualEncoder)]
    [InlineData(EvidenceMismatch.HardwareRange)]
    [InlineData(EvidenceMismatch.HardwareFlag)]
    [InlineData(EvidenceMismatch.Adapter)]
    [InlineData(EvidenceMismatch.InputFormat)]
    [InlineData(EvidenceMismatch.InvalidInputFormat)]
    [InlineData(EvidenceMismatch.Width)]
    [InlineData(EvidenceMismatch.Height)]
    [InlineData(EvidenceMismatch.FrameRateNumerator)]
    [InlineData(EvidenceMismatch.FrameRateDenominator)]
    [InlineData(EvidenceMismatch.Validation)]
    public void EvidenceParserRejectsEveryMismatchedField(
        EvidenceMismatch mismatch)
    {
        using var fixture = new NativeEvidenceFixture(EncoderKind.Nvenc);
        switch (mismatch)
        {
            case EvidenceMismatch.StructSize:
                fixture.Native.StructSize--;
                break;
            case EvidenceMismatch.AbiVersion:
                fixture.Native.AbiVersion++;
                break;
            case EvidenceMismatch.Reserved:
                fixture.Native.Reserved = 1;
                break;
            case EvidenceMismatch.ActualEncoder:
                fixture.Native.ActualEncoderKind = NativeEncoderKind.Amf;
                break;
            case EvidenceMismatch.InvalidActualEncoder:
                fixture.Native.ActualEncoderKind = (NativeEncoderKind)999;
                break;
            case EvidenceMismatch.HardwareRange:
                fixture.Native.HardwareAccelerated = 2;
                break;
            case EvidenceMismatch.HardwareFlag:
                fixture.Native.HardwareAccelerated = 0;
                break;
            case EvidenceMismatch.Adapter:
                fixture.Native.AdapterLuid++;
                break;
            case EvidenceMismatch.InputFormat:
                fixture.Native.OpenedInputFormat =
                    NativeEncoderInputFormat.QsvNv12;
                break;
            case EvidenceMismatch.InvalidInputFormat:
                fixture.Native.OpenedInputFormat =
                    (NativeEncoderInputFormat)999;
                break;
            case EvidenceMismatch.Width:
                fixture.Native.Width++;
                break;
            case EvidenceMismatch.Height:
                fixture.Native.Height++;
                break;
            case EvidenceMismatch.FrameRateNumerator:
                fixture.Native.FramesPerSecondNumerator++;
                break;
            case EvidenceMismatch.FrameRateDenominator:
                fixture.Native.FramesPerSecondDenominator++;
                break;
            case EvidenceMismatch.Validation:
                fixture.Native.ValidationFlags =
                    NativeEncoderProbeValidation.None;
                break;
            default:
                throw new InvalidOperationException(
                    "The evidence mismatch is not supported.");
        }

        Assert.Throws<NativeEncoderProbeException>(() =>
            PInvokeEncoderProbe.ReadEvidence(
                fixture.Request,
                fixture.Native,
                fixture.Buffer,
                fixture.BufferSize));
    }

    [Fact]
    public void EvidenceParserRejectsWrongCodecAndTrailingPayload()
    {
        using (var wrongCodec = new NativeEvidenceFixture(
                   EncoderKind.MediaFoundationSoftware,
                   codecName: "invalid"))
        {
            Assert.Throws<NativeEncoderProbeException>(() =>
                PInvokeEncoderProbe.ReadEvidence(
                    wrongCodec.Request,
                    wrongCodec.Native,
                    wrongCodec.Buffer,
                    wrongCodec.BufferSize));
        }

        using var trailing = new NativeEvidenceFixture(EncoderKind.Nvenc);
        Assert.Throws<NativeEncoderProbeException>(() =>
            PInvokeEncoderProbe.ReadEvidence(
                trailing.Request,
                trailing.Native,
                trailing.Buffer,
                trailing.BufferSize + 1));
    }

    [Theory]
    [InlineData(EvidenceTextFailure.ZeroSize)]
    [InlineData(EvidenceTextFailure.OffsetMismatch)]
    [InlineData(EvidenceTextFailure.OffsetBeyondBuffer)]
    [InlineData(EvidenceTextFailure.SizeBeyondBuffer)]
    [InlineData(EvidenceTextFailure.InvalidUtf8)]
    [InlineData(EvidenceTextFailure.Whitespace)]
    [InlineData(EvidenceTextFailure.ControlCharacter)]
    public void EvidenceParserRejectsInvalidText(
        EvidenceTextFailure failure)
    {
        byte[] bytes = failure switch
        {
            EvidenceTextFailure.InvalidUtf8 => [0xff],
            EvidenceTextFailure.Whitespace => " "u8.ToArray(),
            EvidenceTextFailure.ControlCharacter => "a\n"u8.ToArray(),
            _ => "x"u8.ToArray(),
        };
        var buffer = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            uint nextOffset = failure switch
            {
                EvidenceTextFailure.OffsetMismatch => 1,
                EvidenceTextFailure.OffsetBeyondBuffer => 2,
                _ => 0,
            };
            uint offset = failure == EvidenceTextFailure.OffsetBeyondBuffer
                ? 2u
                : 0u;
            uint size = failure switch
            {
                EvidenceTextFailure.ZeroSize => 0,
                EvidenceTextFailure.SizeBeyondBuffer =>
                    checked((uint)bytes.Length + 1),
                _ => checked((uint)bytes.Length),
            };

            Assert.Throws<NativeEncoderProbeException>(() =>
                PInvokeEncoderProbe.ReadText(
                    buffer,
                    checked((uint)bytes.Length),
                    ref nextOffset,
                    offset,
                    size,
                    "test field"));
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [Fact]
    public void NativeMappingsCoverEveryValueAndRejectUnknowns()
    {
        Assert.Equal(
            EncoderKind.Nvenc,
            PInvokeEncoderProbe.ConvertEncoder(NativeEncoderKind.Nvenc));
        Assert.Equal(
            EncoderKind.Amf,
            PInvokeEncoderProbe.ConvertEncoder(NativeEncoderKind.Amf));
        Assert.Equal(
            EncoderKind.Qsv,
            PInvokeEncoderProbe.ConvertEncoder(NativeEncoderKind.Qsv));
        Assert.Equal(
            EncoderKind.MediaFoundationSoftware,
            PInvokeEncoderProbe.ConvertEncoder(
                NativeEncoderKind.MediaFoundationSoftware));
        Assert.Equal(
            EncoderInputFormat.SystemMemoryNv12,
            PInvokeEncoderProbe.ConvertInputFormat(
                NativeEncoderInputFormat.SystemMemoryNv12));
        Assert.Equal(
            EncoderInputFormat.D3d11Nv12,
            PInvokeEncoderProbe.ConvertInputFormat(
                NativeEncoderInputFormat.D3d11Nv12));
        Assert.Equal(
            EncoderInputFormat.QsvNv12,
            PInvokeEncoderProbe.ConvertInputFormat(
                NativeEncoderInputFormat.QsvNv12));
        foreach (var encoder in Enum.GetValues<EncoderKind>())
        {
            _ = PInvokeEncoderProbe.ConvertEncoder(encoder);
            _ = PInvokeEncoderProbe.ExpectedCodec(encoder);
        }

        Assert.True(PInvokeEncoderProbe.IsFallbackStatus(
            NativeStatus.BackendUnavailable));
        Assert.True(PInvokeEncoderProbe.IsFallbackStatus(
            NativeStatus.InternalError));
        Assert.True(PInvokeEncoderProbe.IsFallbackStatus(NativeStatus.Timeout));
        Assert.False(PInvokeEncoderProbe.IsFallbackStatus(NativeStatus.Ok));
        Assert.Throws<NativeEncoderProbeException>(() =>
            PInvokeEncoderProbe.ConvertEncoder((NativeEncoderKind)999));
        Assert.Throws<NativeEncoderProbeException>(() =>
            PInvokeEncoderProbe.ConvertInputFormat(
                (NativeEncoderInputFormat)999));
        Assert.Throws<NativeEncoderProbeException>(() =>
            PInvokeEncoderProbe.ExpectedCodec((EncoderKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PInvokeEncoderProbe.ConvertEncoder((EncoderKind)999));
    }

    [Fact]
    public async Task CancellationWhileNativeProbeRunsReturnsToCaller()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeEncoderProbeFixtureControls(FixturePath());
        controls.Reset();
        controls.SetResult(status: 0, packetProduced: true);
        controls.SetVerifiedSoftwareEvidence(Request());
        controls.BlockNextProbe();
        await using var probe = new PInvokeEncoderProbe(FixturePath());
        using var cancellation = new CancellationTokenSource();
        var probing = probe.ProbeAsync(Request(), cancellation.Token);
        await controls.WaitUntilProbeEnteredAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                probing).WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            controls.ReleaseProbe();
        }

        await probe.DisposeAsync();
        Assert.Equal(1u, controls.CallCount());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task UnexpectedNativeSizeQueryStatusFailsClosed(int status)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var controls = new NativeEncoderProbeFixtureControls(FixturePath());
        controls.Reset();
        controls.SetResult(status, packetProduced: false);
        using var probe = new PInvokeEncoderProbe(FixturePath());

        await Assert.ThrowsAsync<NativeEncoderProbeException>(() =>
            probe.ProbeAsync(Request(), CancellationToken.None));
    }

    public enum EvidenceMismatch
    {
        StructSize,
        AbiVersion,
        Reserved,
        ActualEncoder,
        InvalidActualEncoder,
        HardwareRange,
        HardwareFlag,
        Adapter,
        InputFormat,
        InvalidInputFormat,
        Width,
        Height,
        FrameRateNumerator,
        FrameRateDenominator,
        Validation,
    }

    public enum EvidenceTextFailure
    {
        ZeroSize,
        OffsetMismatch,
        OffsetBeyondBuffer,
        SizeBeyondBuffer,
        InvalidUtf8,
        Whitespace,
        ControlCharacter,
    }

    private static EncoderProbeRequest Request() => new(
        EncoderKind.MediaFoundationSoftware,
        adapterLuid: 1,
        "software-probe-adapter",
        1280,
        720,
        new FrameRate(30));

    private sealed class NativeEvidenceFixture : IDisposable
    {
        public NativeEvidenceFixture(
            EncoderKind encoder,
            string? codecName = null)
        {
            Request = new EncoderProbeRequest(
                encoder,
                adapterLuid: 42,
                "evidence-adapter",
                1280,
                720,
                new FrameRate(30));
            var nativeEncoder = encoder switch
            {
                EncoderKind.Nvenc => NativeEncoderKind.Nvenc,
                EncoderKind.Amf => NativeEncoderKind.Amf,
                EncoderKind.Qsv => NativeEncoderKind.Qsv,
                EncoderKind.MediaFoundationSoftware =>
                    NativeEncoderKind.MediaFoundationSoftware,
                _ => throw new ArgumentOutOfRangeException(nameof(encoder)),
            };
            var inputFormat = encoder switch
            {
                EncoderKind.Nvenc or EncoderKind.Amf =>
                    NativeEncoderInputFormat.D3d11Nv12,
                EncoderKind.Qsv => NativeEncoderInputFormat.QsvNv12,
                EncoderKind.MediaFoundationSoftware =>
                    NativeEncoderInputFormat.SystemMemoryNv12,
                _ => throw new ArgumentOutOfRangeException(nameof(encoder)),
            };
            ExpectedCodec = encoder switch
            {
                EncoderKind.Nvenc => "h264_nvenc",
                EncoderKind.Amf => "h264_amf",
                EncoderKind.Qsv => "h264_qsv",
                EncoderKind.MediaFoundationSoftware => "h264_mf",
                _ => throw new ArgumentOutOfRangeException(nameof(encoder)),
            };
            var values = new[]
            {
                codecName ?? ExpectedCodec,
                "driver|identity",
                "ffmpeg|identity",
                "high",
                "device|identity",
            };
            var encoded = values.Select(Encoding.UTF8.GetBytes).ToArray();
            var bytes = encoded.SelectMany(value => value).ToArray();
            BufferSize = checked((uint)bytes.Length);
            Buffer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, Buffer, bytes.Length);
            uint offset = 0;
            (uint Offset, uint Size) Next(byte[] value)
            {
                var result = (offset, checked((uint)value.Length));
                offset = checked(offset + result.Item2);
                return result;
            }

            var codec = Next(encoded[0]);
            var driver = Next(encoded[1]);
            var ffmpeg = Next(encoded[2]);
            var profile = Next(encoded[3]);
            var device = Next(encoded[4]);
            var hardware = encoder != EncoderKind.MediaFoundationSoftware;
            Native = new NativeEncoderProbeResultV2
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeEncoderProbeResultV2>()),
                AbiVersion = NativeEncoderProbeLibrary.SupportedAbiVersion,
                ActualEncoderKind = nativeEncoder,
                HardwareAccelerated = hardware ? 1u : 0u,
                AdapterLuid = Request.AdapterLuid,
                OpenedInputFormat = inputFormat,
                Width = checked((uint)Request.Width),
                Height = checked((uint)Request.Height),
                FramesPerSecondNumerator =
                    checked((uint)Request.FrameRate.Value),
                FramesPerSecondDenominator = 1,
                ValidationFlags = (NativeEncoderProbeValidation)(uint)(
                    hardware
                        ? EncoderProbeValidation.CompleteHardwarePacket
                        : EncoderProbeValidation.CompleteSoftwarePacket),
                CodecNameOffset = codec.Offset,
                CodecNameSize = codec.Size,
                DriverIdentityOffset = driver.Offset,
                DriverIdentitySize = driver.Size,
                FfmpegBuildIdentityOffset = ffmpeg.Offset,
                FfmpegBuildIdentitySize = ffmpeg.Size,
                ProfileOffset = profile.Offset,
                ProfileSize = profile.Size,
                DeviceIdentityOffset = device.Offset,
                DeviceIdentitySize = device.Size,
            };
        }

        public EncoderProbeRequest Request { get; }

        public string ExpectedCodec { get; }

        public NativeEncoderProbeResultV2 Native;

        public nint Buffer { get; }

        public uint BufferSize { get; }

        public void Dispose() => Marshal.FreeCoTaskMem(Buffer);
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

    private sealed class NativeEncoderProbeFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly ResetDelegate _reset;
        private readonly SetResultDelegate _setResult;
        private readonly SetEvidenceV2Delegate _setEvidenceV2;
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
            _setEvidenceV2 = Resolve<SetEvidenceV2Delegate>(
                "vrrec_test_encoder_probe_set_evidence_v2");
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

        public void SetVerifiedNvencEvidence(EncoderProbeRequest request) =>
            SetEvidence(
                request,
                actualEncoderKind: 1,
                hardwareAccelerated: true,
                openedInputFormat: 2,
                validationFlags: 0x07ff,
                codecName: "h264_nvenc",
                driverIdentity: "nvidia|32.0.16.1062",
                deviceIdentity: "pci\\ven_10de&dev_2684");

        public void SetVerifiedSoftwareEvidence(
            EncoderProbeRequest request) =>
            SetEvidence(
                request,
                actualEncoderKind: 4,
                hardwareAccelerated: false,
                openedInputFormat: 1,
                validationFlags: 0x03ff,
                codecName: "h264_mf",
                driverIdentity: "windows-media-foundation|software",
                deviceIdentity: "software-encoder");

        private void SetEvidence(
            EncoderProbeRequest request,
            uint actualEncoderKind,
            bool hardwareAccelerated,
            uint openedInputFormat,
            uint validationFlags,
            string codecName,
            string driverIdentity,
            string deviceIdentity) =>
            _setEvidenceV2(
                actualEncoderKind,
                hardwareAccelerated ? (byte)1 : (byte)0,
                request.AdapterLuid,
                openedInputFormat,
                checked((uint)request.Width),
                checked((uint)request.Height),
                checked((uint)request.FrameRate.Value),
                1,
                validationFlags,
                codecName,
                driverIdentity,
                "ffmpeg|8.1.2|contract-id",
                "high",
                deviceIdentity);

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
        private delegate void SetEvidenceV2Delegate(
            uint actualEncoderKind,
            byte hardwareAccelerated,
            ulong adapterLuid,
            uint openedInputFormat,
            uint width,
            uint height,
            uint framesPerSecondNumerator,
            uint framesPerSecondDenominator,
            uint validationFlags,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string codecName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string driverIdentity,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string ffmpegBuildIdentity,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string profile,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceIdentity);

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
