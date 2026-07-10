using Microsoft.Win32.SafeHandles;

namespace VRRecorder.Infrastructure.SteamVr.Native;

internal sealed class NativeSteamVrInputSafeHandle
    : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly NativeSteamVrInputLibrary _library;

    public NativeSteamVrInputSafeHandle(
        nint input,
        NativeSteamVrInputLibrary library)
        : base(ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        SetHandle(input);
    }

    protected override bool ReleaseHandle()
    {
        _library.DestroyInput(handle);
        return true;
    }
}
