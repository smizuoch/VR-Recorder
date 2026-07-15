using System.Runtime.InteropServices;
using System.Text;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Encoding;
using VRRecorder.Infrastructure.Media.Native;

namespace VRRecorder.Infrastructure.Media;

public sealed class PInvokeEncoderProbe
    : IEncoderProbe, IDisposable, IAsyncDisposable
{
    private const uint SyntheticFrameCount = 16;
    private const uint MaximumEvidenceUtf8Size = 32_768;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly object _lifetimeGate = new();
    private readonly NativeEncoderProbeLibrary _library;
    private int _activeOperations;
    private bool _disposeStarted;
    private Task? _disposeTask;

    public PInvokeEncoderProbe(string libraryPath)
    {
        _library = new NativeEncoderProbeLibrary(libraryPath);
    }

    public Task<EncoderProbeResult> ProbeAsync(
        EncoderProbeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BeginOperation();
        Task<EncoderProbeResult> operation;
        try
        {
            operation = Task.Run(() =>
            {
                try
                {
                    ThrowIfDisposing();
                    var result = ProbeCore(request);
                    ThrowIfDisposing();
                    return result;
                }
                finally
                {
                    EndOperation();
                }
            });
        }
        catch
        {
            EndOperation();
            throw;
        }

        return WaitForCallerAsync(operation, cancellationToken);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposeStarted = true;
            _disposeTask = Task.Run(DisposeCore);
            return new ValueTask(_disposeTask);
        }
    }

    private void DisposeCore()
    {
        lock (_lifetimeGate)
        {
            while (_activeOperations != 0)
            {
                Monitor.Wait(_lifetimeGate);
            }
        }

        _library.Dispose();
    }

    private EncoderProbeResult ProbeCore(EncoderProbeRequest request)
    {
        var gpuIdentity = Marshal.StringToCoTaskMemUTF8(request.GpuIdentity);
        try
        {
            var config = new NativeEncoderProbeConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeEncoderProbeConfigV1>()),
                AbiVersion = NativeEncoderProbeLibrary.SupportedAbiVersion,
                EncoderKind = ConvertEncoder(request.Encoder),
                SyntheticFrameCount = SyntheticFrameCount,
                AdapterLuid = request.AdapterLuid,
                Width = checked((uint)request.Width),
                Height = checked((uint)request.Height),
                FramesPerSecondNumerator = checked((uint)request.FrameRate.Value),
                FramesPerSecondDenominator = 1,
                GpuIdentityUtf8 = gpuIdentity,
            };
            var nativeResult = EmptyNativeResult();
            var status = _library.ProbeV2(
                ref config,
                ref nativeResult,
                utf8Buffer: 0,
                utf8Capacity: 0,
                out var requiredUtf8Size);
            if (IsFallbackStatus(status))
            {
                return EncoderProbeResult.Failed;
            }
            if (status != NativeStatus.BufferTooSmall ||
                requiredUtf8Size is 0 or > MaximumEvidenceUtf8Size)
            {
                throw Failure(status, "native probe-v2 size query");
            }

            ThrowIfDisposing();
            var utf8Buffer = Marshal.AllocCoTaskMem(
                checked((int)requiredUtf8Size));
            try
            {
                nativeResult = EmptyNativeResult();
                status = _library.ProbeV2(
                    ref config,
                    ref nativeResult,
                    utf8Buffer,
                    requiredUtf8Size,
                    out var secondRequiredUtf8Size);
                if (IsFallbackStatus(status))
                {
                    return EncoderProbeResult.Failed;
                }
                if (status != NativeStatus.Ok ||
                    secondRequiredUtf8Size != requiredUtf8Size)
                {
                    throw Failure(status, "native probe-v2 evidence read");
                }

                return EncoderProbeResult.Verified(ReadEvidence(
                    request,
                    nativeResult,
                    utf8Buffer,
                    requiredUtf8Size));
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8Buffer);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(gpuIdentity);
        }
    }

    private static NativeEncoderProbeResultV2 EmptyNativeResult() => new()
    {
        StructSize = checked((uint)Marshal.SizeOf<
            NativeEncoderProbeResultV2>()),
        AbiVersion = NativeEncoderProbeLibrary.SupportedAbiVersion,
    };

    private static bool IsFallbackStatus(NativeStatus status) =>
        status is NativeStatus.BackendUnavailable or
            NativeStatus.InternalError or
            NativeStatus.Timeout;

    private static EncoderProbeEvidence ReadEvidence(
        EncoderProbeRequest request,
        NativeEncoderProbeResultV2 native,
        nint utf8Buffer,
        uint utf8Size)
    {
        var actualEncoder = ConvertEncoder(native.ActualEncoderKind);
        var inputFormat = ConvertInputFormat(native.OpenedInputFormat);
        var expectedHardware = actualEncoder !=
            EncoderKind.MediaFoundationSoftware;
        var expectedInputFormat = actualEncoder switch
        {
            EncoderKind.Nvenc or EncoderKind.Amf =>
                EncoderInputFormat.D3d11Nv12,
            EncoderKind.Qsv => EncoderInputFormat.QsvNv12,
            EncoderKind.MediaFoundationSoftware =>
                EncoderInputFormat.SystemMemoryNv12,
            _ => throw Failure(
                NativeStatus.InternalError,
                "unknown actual encoder identity"),
        };
        var expectedValidation = expectedHardware
            ? EncoderProbeValidation.CompleteHardwarePacket
            : EncoderProbeValidation.CompleteSoftwarePacket;
        if (native.StructSize != Marshal.SizeOf<NativeEncoderProbeResultV2>() ||
            native.AbiVersion != NativeEncoderProbeLibrary.SupportedAbiVersion ||
            native.Reserved != 0 ||
            actualEncoder != request.Encoder ||
            native.HardwareAccelerated > 1 ||
            (native.HardwareAccelerated == 1) != expectedHardware ||
            native.AdapterLuid != request.AdapterLuid ||
            inputFormat != expectedInputFormat ||
            native.Width != request.Width ||
            native.Height != request.Height ||
            native.FramesPerSecondNumerator != request.FrameRate.Value ||
            native.FramesPerSecondDenominator != 1 ||
            (EncoderProbeValidation)native.ValidationFlags !=
                expectedValidation)
        {
            throw Failure(
                NativeStatus.InternalError,
                "mismatched native probe-v2 evidence");
        }

        uint nextOffset = 0;
        var codecName = ReadText(
            utf8Buffer,
            utf8Size,
            ref nextOffset,
            native.CodecNameOffset,
            native.CodecNameSize,
            "codec name");
        var driverIdentity = ReadText(
            utf8Buffer,
            utf8Size,
            ref nextOffset,
            native.DriverIdentityOffset,
            native.DriverIdentitySize,
            "driver identity");
        var ffmpegBuildIdentity = ReadText(
            utf8Buffer,
            utf8Size,
            ref nextOffset,
            native.FfmpegBuildIdentityOffset,
            native.FfmpegBuildIdentitySize,
            "FFmpeg build identity");
        var profile = ReadText(
            utf8Buffer,
            utf8Size,
            ref nextOffset,
            native.ProfileOffset,
            native.ProfileSize,
            "profile");
        var deviceIdentity = ReadText(
            utf8Buffer,
            utf8Size,
            ref nextOffset,
            native.DeviceIdentityOffset,
            native.DeviceIdentitySize,
            "device identity");
        if (nextOffset != utf8Size || codecName != ExpectedCodec(actualEncoder))
        {
            throw Failure(
                NativeStatus.InternalError,
                "invalid native probe-v2 identity payload");
        }

        return new EncoderProbeEvidence(
            actualEncoder,
            codecName,
            expectedHardware,
            native.AdapterLuid,
            inputFormat,
            checked((int)native.Width),
            checked((int)native.Height),
            request.FrameRate,
            expectedValidation,
            driverIdentity,
            ffmpegBuildIdentity,
            profile,
            deviceIdentity);
    }

    private static string ReadText(
        nint buffer,
        uint bufferSize,
        ref uint nextOffset,
        uint offset,
        uint size,
        string field)
    {
        if (size == 0 || offset != nextOffset || offset > bufferSize ||
            size > bufferSize - offset)
        {
            throw Failure(
                NativeStatus.InternalError,
                $"invalid {field} range");
        }

        var bytes = new byte[checked((int)size)];
        Marshal.Copy(
            buffer + checked((int)offset),
            bytes,
            0,
            bytes.Length);
        string value;
        try
        {
            value = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new NativeEncoderProbeException(
                (int)NativeStatus.InternalError,
                $"Native encoder probe-v2 returned invalid UTF-8 for {field}.",
                exception);
        }
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw Failure(
                NativeStatus.InternalError,
                $"invalid {field} text");
        }
        nextOffset = checked(offset + size);
        return value;
    }

    private static EncoderKind ConvertEncoder(NativeEncoderKind encoder) =>
        encoder switch
        {
            NativeEncoderKind.Nvenc => EncoderKind.Nvenc,
            NativeEncoderKind.Amf => EncoderKind.Amf,
            NativeEncoderKind.Qsv => EncoderKind.Qsv,
            NativeEncoderKind.MediaFoundationSoftware =>
                EncoderKind.MediaFoundationSoftware,
            _ => throw Failure(
                NativeStatus.InternalError,
                "invalid actual encoder kind"),
        };

    private static EncoderInputFormat ConvertInputFormat(
        NativeEncoderInputFormat inputFormat) => inputFormat switch
        {
            NativeEncoderInputFormat.SystemMemoryNv12 =>
                EncoderInputFormat.SystemMemoryNv12,
            NativeEncoderInputFormat.D3d11Nv12 =>
                EncoderInputFormat.D3d11Nv12,
            NativeEncoderInputFormat.QsvNv12 => EncoderInputFormat.QsvNv12,
            _ => throw Failure(
                NativeStatus.InternalError,
                "invalid encoder input format"),
        };

    private static string ExpectedCodec(EncoderKind encoder) => encoder switch
    {
        EncoderKind.Nvenc => "h264_nvenc",
        EncoderKind.Amf => "h264_amf",
        EncoderKind.Qsv => "h264_qsv",
        EncoderKind.MediaFoundationSoftware => "h264_mf",
        _ => throw Failure(
            NativeStatus.InternalError,
            "invalid encoder codec identity"),
    };

    private static async Task<EncoderProbeResult> WaitForCallerAsync(
        Task<EncoderProbeResult> operation,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return await operation.ConfigureAwait(false);
        }

        try
        {
            return await operation
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            _ = operation.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private static NativeEncoderKind ConvertEncoder(EncoderKind encoder) =>
        encoder switch
        {
            EncoderKind.Nvenc => NativeEncoderKind.Nvenc,
            EncoderKind.Amf => NativeEncoderKind.Amf,
            EncoderKind.Qsv => NativeEncoderKind.Qsv,
            EncoderKind.MediaFoundationSoftware =>
                NativeEncoderKind.MediaFoundationSoftware,
            _ => throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "The encoder kind is unsupported by native probe ABI v1."),
        };

    private void BeginOperation()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
            _activeOperations++;
        }
    }

    private void EndOperation()
    {
        lock (_lifetimeGate)
        {
            _activeOperations--;
            Monitor.PulseAll(_lifetimeGate);
        }
    }

    private void ThrowIfDisposing()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
        }
    }

    private static NativeEncoderProbeException Failure(
        NativeStatus status,
        string operation) =>
        new(
            (int)status,
            $"Native encoder {operation} failed with status {(int)status} ({status}).");
}
