using System.Runtime.InteropServices;

namespace VRRecorder.Infrastructure.Osc;

internal sealed partial class WindowsDnsServiceInterop
    : IWindowsDnsServiceInterop
{
    private const int DnsFreeRecordList = 1;

    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10);

    public uint Browse(nint request, nint cancelHandle)
    {
        EnsureSupported();
        return DnsServiceBrowse(request, cancelHandle);
    }

    public uint BrowseCancel(nint cancelHandle)
    {
        EnsureSupported();
        return DnsServiceBrowseCancel(cancelHandle);
    }

    public uint Resolve(nint request, nint cancelHandle)
    {
        EnsureSupported();
        return DnsServiceResolve(request, cancelHandle);
    }

    public uint ResolveCancel(nint cancelHandle)
    {
        EnsureSupported();
        return DnsServiceResolveCancel(cancelHandle);
    }

    public void FreeRecordList(nint records)
    {
        EnsureSupported();
        DnsFree(records, DnsFreeRecordList);
    }

    public void FreeServiceInstance(nint serviceInstance)
    {
        EnsureSupported();
        DnsServiceFreeInstance(serviceInstance);
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Windows DNS-SD requires Windows 10 or later.");
        }
    }

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceBrowse")]
    private static partial uint DnsServiceBrowse(
        nint request,
        nint cancelHandle);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceBrowseCancel")]
    private static partial uint DnsServiceBrowseCancel(nint cancelHandle);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceResolve")]
    private static partial uint DnsServiceResolve(
        nint request,
        nint cancelHandle);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceResolveCancel")]
    private static partial uint DnsServiceResolveCancel(nint cancelHandle);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsFree")]
    private static partial void DnsFree(nint data, int freeType);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsServiceFreeInstance")]
    private static partial void DnsServiceFreeInstance(nint serviceInstance);
}
