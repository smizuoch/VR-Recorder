using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeEncoderProbeLibrary : IDisposable
{
    public const uint SupportedAbiVersion = 1;
    private readonly nint _library;
    private readonly ProbeDelegate _probe;
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
            _probe = Resolve<ProbeDelegate>("vrrec_encoder_probe_v1");
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
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    public NativeStatus Probe(
        ref NativeEncoderProbeConfigV1 config,
        out byte packetProduced) =>
        _probe(ref config, out packetProduced);

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
    private delegate NativeStatus ProbeDelegate(
        ref NativeEncoderProbeConfigV1 config,
        out byte packetProduced);
}
