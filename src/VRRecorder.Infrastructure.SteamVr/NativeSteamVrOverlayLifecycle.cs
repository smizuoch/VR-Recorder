using System.Runtime.InteropServices;
using VRRecorder.Infrastructure.SteamVr.Native;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class NativeSteamVrOverlayLifecycle : IDisposable
{
    public const string StableOverlayKey =
        OpenVrApplicationManifest.StableAppKey + ".wrist";
    public const string StableOverlayName = "VR Recorder Wrist";
    public const float DefaultWidthInMeters = 0.22F;
    public const float MinimumWidthInMeters = 0.18F;
    public const float MaximumWidthInMeters = 0.32F;

    private readonly object _lifetimeGate = new();
    private readonly NativeSteamVrLibrary _library;
    private readonly NativeSteamVrOverlaySafeHandle _overlay;
    private bool _disposed;

    public NativeSteamVrOverlayLifecycle(
        string libraryPath,
        string installRoot,
        float widthInMeters = DefaultWidthInMeters)
    {
        if (!float.IsFinite(widthInMeters) ||
            widthInMeters < MinimumWidthInMeters ||
            widthInMeters > MaximumWidthInMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(widthInMeters),
                widthInMeters,
                "The SteamVR overlay width must be between 0.18 and 0.32 metres.");
        }

        var applicationManifest =
            OpenVrApplicationManifest.ResolveAndValidate(installRoot);
        _library = new NativeSteamVrLibrary(libraryPath);
        try
        {
            _overlay = CreateOverlay(
                _library,
                applicationManifest.ManifestPath,
                widthInMeters);
        }
        catch
        {
            _library.Dispose();
            throw;
        }
    }

    public void Show() => Operate(
        static (library, overlay) => library.ShowOverlay(overlay),
        "show");

    public void Hide() => Operate(
        static (library, overlay) => library.HideOverlay(overlay),
        "hide");

    public void Close() => Operate(
        static (library, overlay) => library.CloseOverlay(overlay),
        "close");

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _overlay.Dispose();
            }
            finally
            {
                _library.Dispose();
            }
        }
    }

    private static NativeSteamVrOverlaySafeHandle CreateOverlay(
        NativeSteamVrLibrary library,
        string applicationManifestPath,
        float widthInMeters)
    {
        var manifestPath = Marshal.StringToCoTaskMemUTF8(
            applicationManifestPath);
        var overlayKey = Marshal.StringToCoTaskMemUTF8(StableOverlayKey);
        var overlayName = Marshal.StringToCoTaskMemUTF8(StableOverlayName);
        try
        {
            var config = new NativeSteamVrOverlayConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrOverlayConfigV1>()),
                AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
                ApplicationManifestPathUtf8 = manifestPath,
                OverlayKeyUtf8 = overlayKey,
                OverlayNameUtf8 = overlayName,
                WidthInMeters = widthInMeters,
            };
            var status = library.CreateOverlay(ref config, out var overlay);
            if (status != NativeSteamVrStatus.Ok || overlay == 0)
            {
                throw Failure(
                    status == NativeSteamVrStatus.Ok
                        ? NativeSteamVrStatus.InternalError
                        : status,
                    "create");
            }

            return new NativeSteamVrOverlaySafeHandle(overlay, library);
        }
        finally
        {
            Marshal.FreeCoTaskMem(overlayName);
            Marshal.FreeCoTaskMem(overlayKey);
            Marshal.FreeCoTaskMem(manifestPath);
        }
    }

    private void Operate(
        Func<NativeSteamVrLibrary, nint, NativeSteamVrStatus> operation,
        string operationName)
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var status = operation(
                _library,
                _overlay.DangerousGetHandle());
            if (status != NativeSteamVrStatus.Ok)
            {
                throw Failure(status, operationName);
            }
        }
    }

    private static SteamVrOverlayException Failure(
        NativeSteamVrStatus status,
        string operation) => new((int)status, operation);
}
