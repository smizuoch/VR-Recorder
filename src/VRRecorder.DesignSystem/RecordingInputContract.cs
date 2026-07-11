namespace VRRecorder.DesignSystem;

public static class RecordingInputContract
{
    public const string SteamVrActionSetPath = "/actions/vrrecorder";

    public const string SteamVrToggleActionPath =
        "/actions/vrrecorder/in/toggle_recording";

    public const string SteamVrToggleMicrophoneActionPath =
        "/actions/vrrecorder/in/toggle_microphone";

    public static UiCommandId Resolve(UiActivationKind activationKind) =>
        activationKind switch
        {
            UiActivationKind.DesktopClick or
            UiActivationKind.DesktopKeyboard or
            UiActivationKind.DesktopTray or
            UiActivationKind.WristRay or
            UiActivationKind.SteamVrAction => UiCommandId.ToggleRecording,
            _ => throw new ArgumentOutOfRangeException(
                nameof(activationKind),
                activationKind,
                "Unknown UI activation kind."),
        };
}
