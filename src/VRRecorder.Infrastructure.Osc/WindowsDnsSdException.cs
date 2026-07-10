namespace VRRecorder.Infrastructure.Osc;

public sealed class WindowsDnsSdException : IOException
{
    public WindowsDnsSdException(string operation, uint status)
        : base($"Windows DNS-SD {operation} failed with status {status}.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Operation = operation;
        Status = status;
    }

    public string Operation { get; }

    public uint Status { get; }
}
