using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeEncoderProbeLibrary : IDisposable
{
    public const uint SupportedAbiVersion = 1;
    private readonly nint _library;
    private readonly ProbeV2Delegate _probeV2;
    private int _disposed;

    public NativeEncoderProbeLibrary(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        if (!Path.IsPathFullyQualified(libraryPath))
        {
            throw new ArgumentException(
                "The native encoder probe library path must be absolute.",
                nameof(libraryPath));
        }

        var fullPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The native encoder probe library was not found.",
                fullPath);
        }

        _library = NativeLibrary.Load(fullPath);
        try
        {
            var abiVersion = Resolve<AbiVersionDelegate>("vrrec_abi_version");
            _probeV2 = Resolve<ProbeV2Delegate>("vrrec_encoder_probe_v2");
            var actualVersion = abiVersion();
            if (actualVersion != SupportedAbiVersion)
            {
                throw new InvalidOperationException(
                    $"Native ABI {actualVersion} is not supported; expected {SupportedAbiVersion}.");
            }

            if (Marshal.SizeOf<NativeEncoderProbeConfigV1>() != 56)
            {
                throw new TypeLoadException(
                    "Managed encoder probe layout does not match native ABI v1.");
            }
            if (Marshal.SizeOf<NativeEncoderProbeResultV2>() != 96)
            {
                throw new TypeLoadException(
                    "Managed encoder probe result layout does not match native ABI v2.");
            }
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    public NativeStatus ProbeV2(
        ref NativeEncoderProbeConfigV1 config,
        ref NativeEncoderProbeResultV2 result,
        nint utf8Buffer,
        uint utf8Capacity,
        out uint requiredUtf8Size) =>
        _probeV2(
            ref config,
            ref result,
            utf8Buffer,
            utf8Capacity,
            out requiredUtf8Size);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            NativeLibrary.Free(_library);
        }
    }

    private TDelegate Resolve<TDelegate>(string exportName)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(_library, exportName));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeStatus ProbeV2Delegate(
        ref NativeEncoderProbeConfigV1 config,
        ref NativeEncoderProbeResultV2 result,
        nint utf8Buffer,
        uint utf8Capacity,
        out uint requiredUtf8Size);
}
