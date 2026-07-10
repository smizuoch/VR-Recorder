using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopRecordingUiSnapshot(
    long Revision,
    RecorderState State,
    string StatusTextResourceKey,
    string StatusAccessibleDescriptionResourceKey,
    string ActionLabelResourceKey,
    string ActionAccessibleNameResourceKey,
    string ActionHelpResourceKey,
    bool IsActionEnabled);
