namespace VRRecorder.Application.Desktop;

public sealed record DesktopRecordingHostActivation(
    DesktopRecordingHostState State,
    DesktopRecordingInitializationFailure? Failure);
