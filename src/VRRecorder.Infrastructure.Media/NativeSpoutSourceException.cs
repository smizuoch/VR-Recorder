namespace VRRecorder.Infrastructure.Media;

public sealed class NativeSpoutSourceException : Exception
{
    public NativeSpoutSourceException(int status, string message)
        : base(message)
    {
        Status = status;
    }

    public NativeSpoutSourceException(
        int status,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Status = status;
    }

    public int Status { get; }
}
