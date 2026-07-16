using Microsoft.Win32.SafeHandles;

namespace VRRecorder.Infrastructure.SteamVr.Native;

internal sealed class NativeSteamVrHapticSafeHandle
    : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly NativeSteamVrLibrary _library;

    public NativeSteamVrHapticSafeHandle(
        nint haptic,
        NativeSteamVrLibrary library)
        : base(ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        SetHandle(haptic);
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            _library.DestroyHaptic(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
