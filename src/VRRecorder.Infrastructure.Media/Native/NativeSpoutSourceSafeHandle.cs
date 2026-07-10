using Microsoft.Win32.SafeHandles;

namespace VRRecorder.Infrastructure.Media.Native;

internal sealed class NativeSpoutSourceSafeHandle
    : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly NativeSpoutSourceLibrary _library;

    public NativeSpoutSourceSafeHandle(
        nint source,
        NativeSpoutSourceLibrary library)
        : base(ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        SetHandle(source);
    }

    protected override bool ReleaseHandle()
    {
        var source = handle;
        _library.DestroySource(ref source);
        return source == 0;
    }
}
