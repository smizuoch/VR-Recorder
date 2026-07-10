namespace VRRecorder.Infrastructure.Osc;

public delegate void WindowsDnsServiceBrowseCallback(
    uint status,
    IReadOnlyList<string> serviceInstanceNames);

public delegate void WindowsDnsServiceResolveCallback(
    uint status,
    WindowsDnsSdResolvedService? resolvedService);

public interface IWindowsDnsServiceNativeApi
{
    bool IsSupported { get; }

    IWindowsDnsServiceOperation StartBrowse(
        string queryName,
        WindowsDnsServiceBrowseCallback callback);

    IWindowsDnsServiceOperation StartResolve(
        string serviceInstanceName,
        WindowsDnsServiceResolveCallback callback);
}

public interface IWindowsDnsServiceOperation : IDisposable
{
    void Cancel();
}
