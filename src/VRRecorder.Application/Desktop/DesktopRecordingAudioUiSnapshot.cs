using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopRecordingAudioUiSnapshot(
    long Revision,
    RecorderState State,
    bool IsVisible,
    bool IsEnabled,
    bool IsMicrophoneSelected,
    bool IsMuteAllSelected,
    string MicrophoneLabelResourceKey,
    string MicrophoneAccessibleNameResourceKey,
    string MicrophoneHelpResourceKey,
    string MuteAllLabelResourceKey,
    string MuteAllAccessibleNameResourceKey,
    string MuteAllHelpResourceKey);
