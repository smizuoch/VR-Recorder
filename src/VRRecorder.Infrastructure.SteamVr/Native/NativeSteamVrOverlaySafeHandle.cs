using Microsoft.Win32.SafeHandles;

namespace VRRecorder.Infrastructure.SteamVr.Native;

internal sealed class NativeSteamVrOverlaySafeHandle
    : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly NativeSteamVrLibrary _library;

    public NativeSteamVrOverlaySafeHandle(
        nint overlay,
        NativeSteamVrLibrary library)
        : base(ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        SetHandle(overlay);
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            _library.DestroyOverlay(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
