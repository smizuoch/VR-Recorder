using VRRecorder.Application.Settings;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopRecordingSettingsDraft(
    string OutputFolder,
    int SelfTimerSeconds,
    int? AutoStopSeconds,
    ResolutionChangePolicy ResolutionChangePolicy,
    int FrameRate,
    EncoderPreference Encoder,
    VideoQualityPreset QualityPreset);
