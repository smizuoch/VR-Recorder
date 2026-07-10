namespace VRRecorder.Infrastructure.SteamVr;

public interface ISteamVrInputRuntime
{
    IAsyncEnumerable<SteamVrDigitalActionState> ObserveDigitalActionAsync(
        string actionPath,
        CancellationToken cancellationToken);
}
