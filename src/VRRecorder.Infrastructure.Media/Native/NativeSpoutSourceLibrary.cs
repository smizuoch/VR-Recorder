using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeSpoutSourceLibrary : IDisposable
{
    public const uint SupportedAbiVersion = 1;
    public const uint MaximumSnapshotEntries = 1_024;
    public const uint MaximumUtf8BufferSize = 1_048_576;
    public const uint MaximumPollTimeoutMilliseconds = 1_000;
    private readonly nint _library;
    private readonly CreateSourceDelegate _createSource;
    private readonly SnapshotDelegate _snapshot;
    private readonly PollFrameDelegate _pollFrame;
    private readonly DestroySourceDelegate _destroySource;
    private int _disposed;

    public NativeSpoutSourceLibrary(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        if (!Path.IsPathFullyQualified(libraryPath))
        {
            throw new ArgumentException(
                "The native library path must be absolute.",
                nameof(libraryPath));
        }

        var fullPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The native Spout source library was not found.",
                fullPath);
        }

        _library = NativeLibrary.Load(fullPath);
        try
        {
            var abiVersion = Resolve<AbiVersionDelegate>("vrrec_abi_version");
            _createSource = Resolve<CreateSourceDelegate>(
                "vrrec_spout_source_create_v1");
            _snapshot = Resolve<SnapshotDelegate>(
                "vrrec_spout_source_snapshot_v1");
            _pollFrame = Resolve<PollFrameDelegate>(
                "vrrec_spout_source_poll_frame_v1");
            _destroySource = Resolve<DestroySourceDelegate>(
                "vrrec_spout_source_destroy_v1");
            var actualVersion = abiVersion();
            if (actualVersion != SupportedAbiVersion)
            {
                throw new InvalidOperationException(
                    $"Native ABI {actualVersion} is not supported; expected {SupportedAbiVersion}.");
            }

            EnsureManagedLayouts();
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    public NativeStatus CreateSource(
        ref NativeSpoutSourceConfigV1 config,
        out nint source) =>
        _createSource(ref config, out source);

    public NativeStatus Snapshot(
        nint source,
        NativeSpoutSenderSnapshotV1[]? entries,
        byte[]? utf8Buffer,
        out uint entryCount,
        out uint requiredUtf8Size)
    {
        GCHandle entryHandle = default;
        GCHandle utf8Handle = default;
        try
        {
            var entryPointer = Pin(entries, ref entryHandle);
            var utf8Pointer = Pin(utf8Buffer, ref utf8Handle);
            return _snapshot(
                source,
                entryPointer,
                checked((uint)(entries?.Length ?? 0)),
                utf8Pointer,
                checked((uint)(utf8Buffer?.Length ?? 0)),
                out entryCount,
                out requiredUtf8Size);
        }
        finally
        {
            Free(ref utf8Handle);
            Free(ref entryHandle);
        }
    }

    public NativeStatus PollFrame(
        nint source,
        uint timeoutMilliseconds,
        ref NativeSpoutFrameV1 frame,
        byte[]? utf8Buffer,
        out uint requiredUtf8Size)
    {
        GCHandle utf8Handle = default;
        try
        {
            var utf8Pointer = Pin(utf8Buffer, ref utf8Handle);
            return _pollFrame(
                source,
                timeoutMilliseconds,
                ref frame,
                utf8Pointer,
                checked((uint)(utf8Buffer?.Length ?? 0)),
                out requiredUtf8Size);
        }
        finally
        {
            Free(ref utf8Handle);
        }
    }

    public void DestroySource(ref nint source) => _destroySource(ref source);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            NativeLibrary.Free(_library);
        }
    }

    private static nint Pin<T>(T[]? values, ref GCHandle handle)
        where T : struct
    {
        if (values is not { Length: > 0 })
        {
            return 0;
        }

        handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }

    private static nint Pin(byte[]? values, ref GCHandle handle)
    {
        if (values is not { Length: > 0 })
        {
            return 0;
        }

        handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }

    private static void Free(ref GCHandle handle)
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }

    private static void EnsureManagedLayouts()
    {
        if (Marshal.SizeOf<NativeSpoutSourceConfigV1>() != 16 ||
            Marshal.SizeOf<NativeSpoutSenderSnapshotV1>() != 24 ||
            Marshal.SizeOf<NativeSpoutFrameV1>() != 80)
        {
            throw new TypeLoadException(
                "Managed Spout source layouts do not match native ABI v1.");
        }
    }

    private TDelegate Resolve<TDelegate>(string exportName)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(_library, exportName));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeStatus CreateSourceDelegate(
        ref NativeSpoutSourceConfigV1 config,
        out nint source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeStatus SnapshotDelegate(
        nint source,
        nint entries,
        uint entryCapacity,
        nint utf8Buffer,
        uint utf8Capacity,
        out uint entryCount,
        out uint requiredUtf8Size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeStatus PollFrameDelegate(
        nint source,
        uint timeoutMilliseconds,
        ref NativeSpoutFrameV1 frame,
        nint utf8Buffer,
        uint utf8Capacity,
        out uint requiredUtf8Size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroySourceDelegate(ref nint source);
}
