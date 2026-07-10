using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Osc;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate void WindowsDnsServiceBrowseNativeCallback(
    uint status,
    nint queryContext,
    nint records);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate void WindowsDnsServiceResolveNativeCallback(
    uint status,
    nint queryContext,
    nint serviceInstance);

public interface IWindowsDnsServiceInterop
{
    bool IsSupported { get; }

    uint Browse(nint request, nint cancelHandle);

    uint BrowseCancel(nint cancelHandle);

    uint Resolve(nint request, nint cancelHandle);

    uint ResolveCancel(nint cancelHandle);

    void FreeRecordList(nint records);

    void FreeServiceInstance(nint serviceInstance);
}
