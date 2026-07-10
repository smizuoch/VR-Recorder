using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.SteamVr.Native;

internal sealed class NativeSteamVrInputLibrary : IDisposable
{
    public const uint SupportedAbiVersion = 1;
    private readonly nint _library;
    private readonly CreateInputDelegate _createInput;
    private readonly PollInputDelegate _pollInput;
    private readonly DestroyInputDelegate _destroyInput;
    private int _disposed;

    public NativeSteamVrInputLibrary(string libraryPath)
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
                "The native recording library was not found.",
                fullPath);
        }

        _library = NativeLibrary.Load(fullPath);
        try
        {
            var abiVersion = Resolve<AbiVersionDelegate>("vrrec_abi_version");
            _createInput = Resolve<CreateInputDelegate>(
                "vrrec_steamvr_input_create_v1");
            _pollInput = Resolve<PollInputDelegate>(
                "vrrec_steamvr_input_poll_v1");
            _destroyInput = Resolve<DestroyInputDelegate>(
                "vrrec_steamvr_input_destroy_v1");
            var actualVersion = abiVersion();
            if (actualVersion != SupportedAbiVersion)
            {
                throw new InvalidOperationException(
                    $"Native ABI {actualVersion} is not supported; expected {SupportedAbiVersion}.");
            }
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    public NativeSteamVrStatus CreateInput(
        ref NativeSteamVrInputConfigV1 config,
        out nint input) =>
        _createInput(ref config, out input);

    public NativeSteamVrStatus PollInput(
        nint input,
        ref NativeSteamVrDigitalStateV1 state) =>
        _pollInput(input, ref state);

    public void DestroyInput(nint input) => _destroyInput(input);

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
    private delegate NativeSteamVrStatus CreateInputDelegate(
        ref NativeSteamVrInputConfigV1 config,
        out nint input);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSteamVrStatus PollInputDelegate(
        nint input,
        ref NativeSteamVrDigitalStateV1 state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyInputDelegate(nint input);
}
