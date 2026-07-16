namespace VRRecorder.Infrastructure.SteamVr;

public sealed class SteamVrHapticException : Exception
{
    public SteamVrHapticException(int status, string operation)
        : base($"SteamVR haptic {operation} failed with native status {status}.")
    {
        Status = status;
        Operation = operation;
    }

    public int Status { get; }

    public string Operation { get; }
}
