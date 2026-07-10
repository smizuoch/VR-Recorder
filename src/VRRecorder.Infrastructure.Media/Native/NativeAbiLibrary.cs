using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeAbiLibrary : IDisposable
{
    public const uint SupportedAbiVersion = 1;
    private readonly nint _library;
    private readonly CreateSessionDelegate _createSession;
    private readonly SessionOperationDelegate _startSession;
    private readonly SessionOperationDelegate _requestStop;
    private readonly SessionOperationDelegate _abortSession;
    private readonly DestroySessionDelegate _destroySession;
    private int _disposed;

    public NativeAbiLibrary(string libraryPath)
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
            _createSession = Resolve<CreateSessionDelegate>(
                "vrrec_session_create_v1");
            _startSession = Resolve<SessionOperationDelegate>(
                "vrrec_session_start_v1");
            _requestStop = Resolve<SessionOperationDelegate>(
                "vrrec_session_request_stop_v1");
            _abortSession = Resolve<SessionOperationDelegate>(
                "vrrec_session_abort_v1");
            _destroySession = Resolve<DestroySessionDelegate>(
                "vrrec_session_destroy_v1");
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

    public NativeStatus CreateSession(
        ref NativeSessionConfigV1 config,
        ref NativeCallbacksV1 callbacks,
        out nint session) =>
        _createSession(ref config, ref callbacks, out session);

    public NativeStatus StartSession(nint session) => _startSession(session);

    public NativeStatus RequestStop(nint session) => _requestStop(session);

    public NativeStatus AbortSession(nint session) => _abortSession(session);

    public void DestroySession(nint session) => _destroySession(session);

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
    private delegate NativeStatus CreateSessionDelegate(
        ref NativeSessionConfigV1 config,
        ref NativeCallbacksV1 callbacks,
        out nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeStatus SessionOperationDelegate(nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroySessionDelegate(nint session);
}
