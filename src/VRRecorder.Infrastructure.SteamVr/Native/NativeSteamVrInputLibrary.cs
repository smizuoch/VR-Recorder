using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.SteamVr.Native;

internal sealed class NativeSteamVrLibrary : IDisposable
{
    public const uint SupportedAbiVersion = 1;
    private readonly nint _library;
    private readonly CreateInputDelegate _createInput;
    private readonly PollInputDelegate _pollInput;
    private readonly DestroyInputDelegate _destroyInput;
    private readonly CreateOverlayDelegate _createOverlay;
    private readonly OverlayOperationDelegate _showOverlay;
    private readonly OverlayOperationDelegate _hideOverlay;
    private readonly UpdateOverlayBgraDelegate _updateOverlayBgra;
    private readonly OverlayOperationDelegate _clearOverlayTexture;
    private readonly PollOverlayPointerEventDelegate _pollOverlayPointerEvent;
    private readonly OverlayPoseDelegate _setOverlayPose;
    private readonly OverlayPoseDelegate _getOverlayPose;
    private readonly OverlayOperationDelegate _closeOverlay;
    private readonly DestroyOverlayDelegate _destroyOverlay;
    private int _disposed;

    public NativeSteamVrLibrary(string libraryPath)
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
            _createOverlay = Resolve<CreateOverlayDelegate>(
                "vrrec_steamvr_overlay_create_v1");
            _showOverlay = Resolve<OverlayOperationDelegate>(
                "vrrec_steamvr_overlay_show_v1");
            _hideOverlay = Resolve<OverlayOperationDelegate>(
                "vrrec_steamvr_overlay_hide_v1");
            _updateOverlayBgra = Resolve<UpdateOverlayBgraDelegate>(
                "vrrec_steamvr_overlay_update_bgra_v1");
            _clearOverlayTexture = Resolve<OverlayOperationDelegate>(
                "vrrec_steamvr_overlay_clear_texture_v1");
            _pollOverlayPointerEvent = Resolve<PollOverlayPointerEventDelegate>(
                "vrrec_steamvr_overlay_poll_pointer_event_v1");
            _setOverlayPose = Resolve<OverlayPoseDelegate>(
                "vrrec_steamvr_overlay_set_pose_v1");
            _getOverlayPose = Resolve<OverlayPoseDelegate>(
                "vrrec_steamvr_overlay_get_pose_v1");
            _closeOverlay = Resolve<OverlayOperationDelegate>(
                "vrrec_steamvr_overlay_close_v1");
            _destroyOverlay = Resolve<DestroyOverlayDelegate>(
                "vrrec_steamvr_overlay_destroy_v1");
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

    public NativeSteamVrStatus CreateOverlay(
        ref NativeSteamVrOverlayConfigV1 config,
        out nint overlay) =>
        _createOverlay(ref config, out overlay);

    public NativeSteamVrStatus ShowOverlay(nint overlay) =>
        _showOverlay(overlay);

    public NativeSteamVrStatus HideOverlay(nint overlay) =>
        _hideOverlay(overlay);

    public NativeSteamVrStatus UpdateOverlayBgra(
        nint overlay,
        ref NativeSteamVrOverlayBgraFrameV1 frame) =>
        _updateOverlayBgra(overlay, ref frame);

    public NativeSteamVrStatus ClearOverlayTexture(nint overlay) =>
        _clearOverlayTexture(overlay);

    public NativeSteamVrStatus PollOverlayPointerEvent(
        nint overlay,
        ref NativeSteamVrOverlayPointerEventV1 pointerEvent) =>
        _pollOverlayPointerEvent(overlay, ref pointerEvent);

    public NativeSteamVrStatus SetOverlayPose(
        nint overlay,
        ref NativeSteamVrOverlayPoseV1 pose) =>
        _setOverlayPose(overlay, ref pose);

    public NativeSteamVrStatus GetOverlayPose(
        nint overlay,
        ref NativeSteamVrOverlayPoseV1 pose) =>
        _getOverlayPose(overlay, ref pose);

    public NativeSteamVrStatus CloseOverlay(nint overlay) =>
        _closeOverlay(overlay);

    public void DestroyOverlay(nint overlay) => _destroyOverlay(overlay);

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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSteamVrStatus CreateOverlayDelegate(
        ref NativeSteamVrOverlayConfigV1 config,
        out nint overlay);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSteamVrStatus OverlayOperationDelegate(nint overlay);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSteamVrStatus UpdateOverlayBgraDelegate(
        nint overlay,
        ref NativeSteamVrOverlayBgraFrameV1 frame);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSteamVrStatus PollOverlayPointerEventDelegate(
        nint overlay,
        ref NativeSteamVrOverlayPointerEventV1 pointerEvent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeSteamVrStatus OverlayPoseDelegate(
        nint overlay,
        ref NativeSteamVrOverlayPoseV1 pose);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyOverlayDelegate(nint overlay);
}
