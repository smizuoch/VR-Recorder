namespace VRRecorder.Infrastructure.Osc;

public interface IOscQueryServiceBrowser
{
    Task<IReadOnlyList<OscQueryServiceAdvertisement>> BrowseAsync(
        CancellationToken cancellationToken);
}
