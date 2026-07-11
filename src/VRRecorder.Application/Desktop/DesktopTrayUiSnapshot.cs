namespace VRRecorder.Application.Desktop;

public sealed record DesktopTrayUiSnapshot(
    long Revision,
    DesktopTrayState State,
    string StateLabelResourceKey,
    string ActionLabelResourceKey,
    bool IsActionEnabled);
