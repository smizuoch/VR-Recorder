namespace VRRecorder.Infrastructure.SteamVr;

public sealed class SteamVrUnavailableException : SteamVrInputException
{
    public SteamVrUnavailableException(int status, string operation)
        : base(status, operation)
    {
    }
}
