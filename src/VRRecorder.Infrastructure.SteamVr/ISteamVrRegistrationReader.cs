namespace VRRecorder.Infrastructure.SteamVr;

public interface ISteamVrRegistrationReader
{
    IReadOnlyList<int?> ReadInstalledMarkers();
}
