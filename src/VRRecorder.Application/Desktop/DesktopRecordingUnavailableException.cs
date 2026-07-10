namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingUnavailableException : InvalidOperationException
{
    public DesktopRecordingUnavailableException(
        DesktopRecordingHostState state,
        DesktopRecordingInitializationFailure? failure)
        : base(failure?.Message ??
               $"Desktop recording is unavailable while the host is {state}.")
    {
        State = state;
        Failure = failure;
    }

    public DesktopRecordingHostState State { get; }

    public DesktopRecordingInitializationFailure? Failure { get; }
}
