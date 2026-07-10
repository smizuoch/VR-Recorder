namespace VRRecorder.Infrastructure.Osc;

public interface IWindowsDnsSdApi
{
    bool IsSupported { get; }

    Task<IReadOnlyList<string>> BrowseAsync(
        string queryName,
        CancellationToken cancellationToken);

    Task<WindowsDnsSdResolvedService?> ResolveAsync(
        string serviceInstanceName,
        CancellationToken cancellationToken);
}
