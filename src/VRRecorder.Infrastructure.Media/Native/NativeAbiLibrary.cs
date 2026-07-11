using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeAbiLibrary : IDisposable
{
    public const uint SupportedAbiVersion = 1;
    private readonly nint _library;
    private readonly CreateSessionDelegate _createSession;
    private readonly SessionOperationDelegate _startSession;
    private readonly UpdateVideoLayoutDelegate _updateVideoLayout;
    private readonly UpdateAudioRoutingDelegate _updateAudioRouting;
    private readonly GetStatisticsDelegate _getStatistics;
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
            _updateVideoLayout = Resolve<UpdateVideoLayoutDelegate>(
                "vrrec_session_update_video_layout_v1");
            _updateAudioRouting = Resolve<UpdateAudioRoutingDelegate>(
                "vrrec_session_update_audio_routing_v1");
            _getStatistics = Resolve<GetStatisticsDelegate>(
                "vrrec_session_get_statistics_v1");
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

    public NativeStatus UpdateVideoLayout(
        nint session,
        ref NativeVideoLayoutV1 layout) =>
        _updateVideoLayout(session, ref layout);

    public NativeStatus GetStatistics(
        nint session,
        ref NativeSessionStatisticsV1 statistics) =>
        _getStatistics(session, ref statistics);

    public NativeStatus UpdateAudioRouting(
        nint session,
        ref NativeAudioRoutingUpdateV1 update) =>
        _updateAudioRouting(session, ref update);

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
    private delegate NativeStatus UpdateVideoLayoutDelegate(
        nint session,
        ref NativeVideoLayoutV1 layout);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeStatus UpdateAudioRoutingDelegate(
        nint session,
        ref NativeAudioRoutingUpdateV1 update);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeStatus GetStatisticsDelegate(
        nint session,
        ref NativeSessionStatisticsV1 statistics);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroySessionDelegate(nint session);
}
