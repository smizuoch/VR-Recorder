namespace VRRecorder.Infrastructure.SteamVr;

public class SteamVrOverlayException : Exception
{
    public SteamVrOverlayException(int status, string operation)
        : base($"SteamVR overlay {operation} failed with native status {status}.")
    {
        Status = status;
        Operation = operation;
    }

    public int Status { get; }

    public string Operation { get; }
}
