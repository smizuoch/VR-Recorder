using System.Runtime.InteropServices;
using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Encoding;
using VRRecorder.Infrastructure.Media.Native;

namespace VRRecorder.Infrastructure.Media;

public sealed class PInvokeEncoderProbe
    : IEncoderProbe, IDisposable, IAsyncDisposable
{
    private const uint SyntheticFrameCount = 16;
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
            var status = _library.Probe(ref config, out var packetProduced);
            return status switch
            {
                NativeStatus.Ok when packetProduced == 1 =>
                    EncoderProbeResult.PacketProduced,
                NativeStatus.Ok when packetProduced == 0 =>
                    EncoderProbeResult.Failed,
                NativeStatus.BackendUnavailable or
                    NativeStatus.InternalError or
                    NativeStatus.Timeout => EncoderProbeResult.Failed,
                NativeStatus.Ok => throw Failure(
                    status,
                    $"invalid packet flag {packetProduced}"),
                _ => throw Failure(status, "native probe call"),
            };
        }
        finally
        {
            Marshal.FreeCoTaskMem(gpuIdentity);
        }
    }

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
