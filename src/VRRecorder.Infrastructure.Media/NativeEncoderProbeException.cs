namespace VRRecorder.Infrastructure.Media;

public sealed class NativeEncoderProbeException : Exception
{
    public NativeEncoderProbeException(int status, string message)
        : base(message)
    {
        Status = status;
    }

    public int Status { get; }
}
