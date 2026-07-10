using Microsoft.Win32.SafeHandles;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeSessionSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly NativeAbiLibrary _library;

    public NativeSessionSafeHandle(
        nint session,
        NativeAbiLibrary library)
        : base(ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        SetHandle(session);
    }

    protected override bool ReleaseHandle()
    {
        _library.DestroySession(handle);
        return true;
    }
}
