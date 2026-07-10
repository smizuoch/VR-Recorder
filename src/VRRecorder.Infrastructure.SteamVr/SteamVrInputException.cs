namespace VRRecorder.Infrastructure.SteamVr;

public class SteamVrInputException : Exception
{
    public SteamVrInputException(
        int status,
        string operation)
        : base($"SteamVR input {operation} failed with native status {status}.")
    {
        Status = status;
        Operation = operation;
    }

    public int Status { get; }

    public string Operation { get; }
}
