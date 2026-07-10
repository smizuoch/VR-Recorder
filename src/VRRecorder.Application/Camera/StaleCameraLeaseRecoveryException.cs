namespace VRRecorder.Application.Camera;

public sealed class StaleCameraLeaseRecoveryException : Exception
{
    public StaleCameraLeaseRecoveryException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
