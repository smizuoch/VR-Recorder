namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingInitializationException : Exception
{
    public DesktopRecordingInitializationException(
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
