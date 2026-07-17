using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media.Native;

namespace VRRecorder.Infrastructure.Media;

public sealed class PInvokeSpoutVideoSource : ISpoutVideoSource, IDisposable
{
    private static readonly TimeSpan DefaultPollSlice =
        TimeSpan.FromMilliseconds(50);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly object _lifetimeGate = new();
    private readonly NativeSpoutSourceLibrary _library;
    private readonly NativeSpoutSourceSafeHandle _source;
    private readonly uint _pollTimeoutMilliseconds;
    private int _activeOperations;
    private bool _disposeStarted;
    private bool _disposeCompleted;

    public PInvokeSpoutVideoSource(string libraryPath)
        : this(libraryPath, DefaultPollSlice)
    {
    }

    public PInvokeSpoutVideoSource(
        string libraryPath,
        TimeSpan pollSlice)
    {
        if (pollSlice < TimeSpan.FromMilliseconds(1) ||
            pollSlice > TimeSpan.FromMilliseconds(
                NativeSpoutSourceLibrary.MaximumPollTimeoutMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollSlice),
                pollSlice,
                "The native Spout poll slice must be between 1 and 1000 milliseconds.");
        }

        _pollTimeoutMilliseconds = checked((uint)Math.Ceiling(
            pollSlice.TotalMilliseconds));
        _library = new NativeSpoutSourceLibrary(libraryPath);
        try
        {
            var config = new NativeSpoutSourceConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSpoutSourceConfigV1>()),
                AbiVersion = NativeSpoutSourceLibrary.SupportedAbiVersion,
            };
            var status = _library.CreateSource(ref config, out var source);
            if (status != NativeStatus.Ok || source == 0)
            {
                throw Failure(
                    status == NativeStatus.Ok
                        ? NativeStatus.InternalError
                        : status,
                    "create");
            }

            _source = new NativeSpoutSourceSafeHandle(source, _library);
        }
        catch
        {
            _library.Dispose();
            throw;
        }
    }

    public Task<IReadOnlyList<SpoutSenderSnapshot>> SnapshotAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginOperation();
        try
        {
            var result = ReadSnapshot();
            ThrowIfDisposing();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SpoutSenderSnapshot>>(result);
        }
        finally
        {
            EndOperation();
        }
    }

    public async IAsyncEnumerable<SpoutFrameObservation> ObserveFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginOperation();
            SpoutFrameObservation? frame;
            try
            {
                // The ABI poll is synchronous. Running each bounded slice on
                // the pool keeps MoveNextAsync asynchronous while preserving
                // a deterministic cancellation upper bound.
                frame = await Task
                    .Run(PollFrame, CancellationToken.None)
                    .ConfigureAwait(false);
                ThrowIfDisposing();
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                EndOperation();
            }

            if (frame is not null)
            {
                yield return frame;
            }
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposeStarted)
            {
                while (!_disposeCompleted)
                {
                    Monitor.Wait(_lifetimeGate);
                }

                return;
            }

            _disposeStarted = true;
            while (_activeOperations != 0)
            {
                Monitor.Wait(_lifetimeGate);
            }
        }

        try
        {
            _source.Dispose();
            _library.Dispose();
        }
        finally
        {
            lock (_lifetimeGate)
            {
                _disposeCompleted = true;
                Monitor.PulseAll(_lifetimeGate);
            }
        }
    }

    private SpoutSenderSnapshot[] ReadSnapshot()
    {
        var source = _source.DangerousGetHandle();
        NativeSpoutSenderSnapshotV1[]? entries = null;
        byte[]? utf8 = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var status = _library.Snapshot(
                source,
                entries,
                utf8,
                out var entryCount,
                out var requiredUtf8Size);
            ValidateRequiredSizes(entryCount, requiredUtf8Size, "snapshot");
            if (status == NativeStatus.Ok)
            {
                if (entries is null && entryCount != 0 ||
                    utf8 is null && requiredUtf8Size != 0 ||
                    entries is not null && entryCount > entries.Length ||
                    utf8 is not null && requiredUtf8Size > utf8.Length)
                {
                    throw Failure(
                        NativeStatus.InternalError,
                        "snapshot returned inconsistent buffer sizes");
                }

                return DecodeSnapshot(
                    entries ?? [],
                    utf8 ?? [],
                    checked((int)entryCount));
            }

            if (status != NativeStatus.BufferTooSmall)
            {
                throw Failure(status, "snapshot");
            }

            entries = CreateSnapshotEntries(entryCount);
            utf8 = new byte[requiredUtf8Size];
        }

        throw Failure(
            NativeStatus.BufferTooSmall,
            "snapshot changed during repeated buffer sizing");
    }

    private SpoutFrameObservation? PollFrame()
    {
        var nativeFrame = CreateFrameOutput();
        var status = _library.PollFrame(
            _source.DangerousGetHandle(),
            _pollTimeoutMilliseconds,
            ref nativeFrame,
            utf8Buffer: null,
            out var requiredUtf8Size);
        if (status == NativeStatus.Timeout)
        {
            return null;
        }

        ValidateRequiredSizes(entryCount: 1, requiredUtf8Size, "frame");
        if (status != NativeStatus.BufferTooSmall)
        {
            throw Failure(status, "frame size query");
        }

        var utf8 = new byte[requiredUtf8Size];
        nativeFrame = CreateFrameOutput();
        status = _library.PollFrame(
            _source.DangerousGetHandle(),
            timeoutMilliseconds: 0,
            ref nativeFrame,
            utf8,
            out var returnedUtf8Size);
        if (status != NativeStatus.Ok)
        {
            throw Failure(status, "frame read");
        }

        if (returnedUtf8Size > utf8.Length)
        {
            throw Failure(
                NativeStatus.InternalError,
                "frame returned an inconsistent UTF-8 buffer size");
        }

        return DecodeFrame(nativeFrame, utf8);
    }

    internal static NativeSpoutSenderSnapshotV1[] CreateSnapshotEntries(
        uint entryCount)
    {
        var entries = new NativeSpoutSenderSnapshotV1[entryCount];
        var structSize = checked((uint)Marshal.SizeOf<
            NativeSpoutSenderSnapshotV1>());
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index].StructSize = structSize;
            entries[index].AbiVersion =
                NativeSpoutSourceLibrary.SupportedAbiVersion;
        }

        return entries;
    }

    internal static NativeSpoutFrameV1 CreateFrameOutput() => new()
    {
        StructSize = checked((uint)Marshal.SizeOf<NativeSpoutFrameV1>()),
        AbiVersion = NativeSpoutSourceLibrary.SupportedAbiVersion,
    };

    internal static SpoutSenderSnapshot[] DecodeSnapshot(
        NativeSpoutSenderSnapshotV1[] entries,
        byte[] utf8,
        int entryCount)
    {
        var result = new SpoutSenderSnapshot[entryCount];
        for (var index = 0; index < result.Length; index++)
        {
            var entry = entries[index];
            ValidateOutputHeader(
                entry.StructSize,
                entry.AbiVersion,
                checked((uint)Marshal.SizeOf<
                    NativeSpoutSenderSnapshotV1>()),
                "snapshot entry");
            var senderId = DecodeUtf8(
                utf8,
                entry.SenderIdOffset,
                entry.SenderIdSize,
                "sender ID");
            result[index] = new SpoutSenderSnapshot(
                senderId,
                entry.LatestFrameGeneration);
        }

        return result;
    }

    internal static SpoutFrameObservation DecodeFrame(
        NativeSpoutFrameV1 frame,
        byte[] utf8)
    {
        ValidateOutputHeader(
            frame.StructSize,
            frame.AbiVersion,
            checked((uint)Marshal.SizeOf<NativeSpoutFrameV1>()),
            "frame");
        if (frame.Reserved != 0)
        {
            throw Failure(
                NativeStatus.InternalError,
                "frame reserved field was non-zero");
        }

        var senderId = DecodeUtf8(
            utf8,
            frame.SenderIdOffset,
            frame.SenderIdSize,
            "sender ID");
        var gpuIdentity = DecodeUtf8(
            utf8,
            frame.GpuIdentityOffset,
            frame.GpuIdentitySize,
            "GPU identity");
        var signal = new StableVideoSignal(
            senderId,
            frame.AdapterLuid,
            gpuIdentity,
            ConvertGpuVendor(frame.GpuVendor),
            checked((int)frame.Width),
            checked((int)frame.Height),
            ConvertPixelFormat(frame.PixelFormat),
            frame.EstimatedSourceFramesPerSecond);
        var receivedAt = MonotonicTimestamp.FromElapsed(
            TimeSpan.FromTicks(checked(
                frame.MonotonicTimestampMicroseconds * 10)));
        return new SpoutFrameObservation(
            signal,
            frame.FrameSequence,
            receivedAt);
    }

    internal static string DecodeUtf8(
        byte[] buffer,
        uint offset,
        uint size,
        string fieldName)
    {
        var end = checked((ulong)offset + size);
        if (size == 0 || end > (ulong)buffer.Length)
        {
            throw Failure(
                NativeStatus.InternalError,
                $"{fieldName} range is outside the packed UTF-8 buffer");
        }

        try
        {
            var value = StrictUtf8.GetString(
                buffer,
                checked((int)offset),
                checked((int)size));
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            {
                throw Failure(
                    NativeStatus.InternalError,
                    $"{fieldName} is empty or contains control text");
            }

            return value;
        }
        catch (DecoderFallbackException exception)
        {
            throw new NativeSpoutSourceException(
                (int)NativeStatus.InternalError,
                $"Native Spout {fieldName} is not valid UTF-8.",
                exception);
        }
    }

    internal static void ValidateOutputHeader(
        uint structSize,
        uint abiVersion,
        uint expectedSize,
        string operation)
    {
        if (structSize < expectedSize ||
            abiVersion != NativeSpoutSourceLibrary.SupportedAbiVersion)
        {
            throw Failure(
                NativeStatus.InternalError,
                $"{operation} returned an incompatible ABI layout");
        }
    }

    internal static void ValidateRequiredSizes(
        uint entryCount,
        uint requiredUtf8Size,
        string operation)
    {
        if (entryCount > NativeSpoutSourceLibrary.MaximumSnapshotEntries ||
            requiredUtf8Size >
                NativeSpoutSourceLibrary.MaximumUtf8BufferSize)
        {
            throw Failure(
                NativeStatus.InternalError,
                $"{operation} exceeded the ABI capacity limits");
        }
    }

    internal static GpuVendor ConvertGpuVendor(NativeGpuVendor vendor) =>
        vendor switch
        {
            NativeGpuVendor.Unknown => GpuVendor.Unknown,
            NativeGpuVendor.Nvidia => GpuVendor.Nvidia,
            NativeGpuVendor.Amd => GpuVendor.Amd,
            NativeGpuVendor.Intel => GpuVendor.Intel,
            _ => throw Failure(
                NativeStatus.InternalError,
                "frame returned an unknown GPU vendor"),
        };

    internal static VideoPixelFormat ConvertPixelFormat(
        NativeSourcePixelFormat pixelFormat) =>
        pixelFormat switch
        {
            NativeSourcePixelFormat.Bgra8 => VideoPixelFormat.Bgra8,
            NativeSourcePixelFormat.Rgba8 => VideoPixelFormat.Rgba8,
            NativeSourcePixelFormat.Nv12 => VideoPixelFormat.Nv12,
            _ => throw Failure(
                NativeStatus.InternalError,
                "frame returned an unknown pixel format"),
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

    private static NativeSpoutSourceException Failure(
        NativeStatus status,
        string operation) =>
        new(
            (int)status,
            $"Native Spout source {operation} failed with status {(int)status} ({status}).");
}
