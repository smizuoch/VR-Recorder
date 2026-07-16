using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Settings;

public sealed record VRRecorderSettings(
    int SchemaVersion,
    RecordingSettings Recording,
    VideoSettings Video,
    AudioSettings Audio,
    VrSettings Vr,
    OscSettings Osc,
    UiLocale UiLocale = UiLocale.System)
{
    public const int CurrentSchemaVersion = 3;
    public const bool DefaultHapticsEnabled = true;
    public const float DefaultHapticFrequencyHertz = 120f;
    public const float DefaultHapticAmplitude = 0.65f;

    public static VRRecorderSettings CreateDefault() =>
        new(
            SchemaVersion: CurrentSchemaVersion,
            Recording: new RecordingSettings(
                OutputFolder: "knownfolder:Downloads",
                SelfTimerSeconds: 0,
                AutoStopSeconds: null,
                ResolutionChangePolicy.SingleFileFit),
            Video: new VideoSettings(
                FrameRate: 30,
                EncoderPreference.Auto,
                VideoQualityPreset.High,
                VideoCodec.H264),
            Audio: new AudioSettings(
                AudioRouting.Mixed,
                DesktopEndpointId: "default-render",
                MicrophoneEndpointId: "default-capture",
                DesktopGainDb: -6,
                MicrophoneGainDb: -6),
            Vr: new VrSettings(
                VrHand.Left,
                OverlayPlacementMode.WristDock,
                WristOverlayPoseContract.CreateDefaultWristDockTransform(),
                PlacementProfiles: [],
                HapticsEnabled: DefaultHapticsEnabled,
                HapticFrequencyHertz: DefaultHapticFrequencyHertz,
                HapticAmplitude: DefaultHapticAmplitude),
            Osc: new OscSettings(
                AutoDiscover: true,
                FallbackHost: "127.0.0.1",
                FallbackSendPort: 9000,
                FallbackReceivePort: 9001),
            UiLocale: UiLocale.System);
}

public sealed record RecordingSettings(
    string OutputFolder,
    int SelfTimerSeconds,
    int? AutoStopSeconds,
    ResolutionChangePolicy ResolutionChangePolicy);

public sealed record VideoSettings(
    int FrameRate,
    EncoderPreference Encoder,
    VideoQualityPreset QualityPreset,
    VideoCodec Codec);

public sealed record AudioSettings(
    AudioRouting Routing,
    string DesktopEndpointId,
    string MicrophoneEndpointId,
    double DesktopGainDb,
    double MicrophoneGainDb);

public sealed record VrSettings(
    VrHand Hand,
    OverlayPlacementMode PlacementMode,
    OverlayTransform Transform,
    IReadOnlyList<VrOverlayPlacementProfile> PlacementProfiles = null!,
    bool HapticsEnabled = false,
    float HapticFrequencyHertz = 0,
    float HapticAmplitude = 0);

public sealed record VrDeviceProfile(
    string TrackingSystemName,
    string HmdModelNumber,
    string ControllerInputProfilePath);

public sealed record VrOverlayPlacementProfile(
    VrDeviceProfile Device,
    VrHand Hand,
    OverlayPlacementMode PlacementMode,
    OverlayTransform Transform);

public sealed record VrOverlayPlacement(
    OverlayPlacementMode PlacementMode,
    OverlayTransform Transform);

public sealed record OverlayTransform(
    double[] Position,
    double[] RotationEuler);

public sealed record OscSettings(
    bool AutoDiscover,
    string FallbackHost,
    int FallbackSendPort,
    int FallbackReceivePort);

public enum ResolutionChangePolicy
{
    SingleFileFit,
    ExactFollowSegments,
}

public enum VideoQualityPreset
{
    Standard,
    High,
}

public enum VideoCodec
{
    H264,
}

public enum VrHand
{
    Left,
    Right,
}

public enum OverlayPlacementMode
{
    WristDock,
    WorldPin,
}
