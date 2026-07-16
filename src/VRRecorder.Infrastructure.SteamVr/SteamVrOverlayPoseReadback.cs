using VRRecorder.Application.Settings;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed record SteamVrOverlayPoseReadback(
    OverlayPlacementMode PlacementMode,
    VrHand? DockHand,
    WristOverlayTrackingOrigin? TrackingOrigin,
    OpenVrMatrix34 Transform);
