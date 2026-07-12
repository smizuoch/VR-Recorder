using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopRecordingSettingsDraft(
    string OutputFolder,
    int SelfTimerSeconds,
    int? AutoStopSeconds,
    ResolutionChangePolicy ResolutionChangePolicy,
    int FrameRate,
    EncoderPreference Encoder,
    VideoQualityPreset QualityPreset,
    AudioRouting AudioRouting,
    double DesktopGainDb,
    double MicrophoneGainDb,
    string DesktopEndpointId = "default-render",
    string MicrophoneEndpointId = "default-capture",
    UiLocale UiLocale = UiLocale.System,
    VrHand VrHand = VrHand.Left,
    OverlayPlacementMode OverlayPlacement = OverlayPlacementMode.WristDock,
    bool OscAutoDiscover = true,
    string OscFallbackHost = "127.0.0.1",
    int OscFallbackSendPort = 9000,
    int OscFallbackReceivePort = 9001);
