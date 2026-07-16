using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Ports;

public interface IWristOverlayPlacementRuntime
{
    VrDeviceProfile ReadDeviceProfile(VrHand hand);

    void ApplyPlacement(
        VrHand hand,
        OverlayPlacementMode placementMode,
        OverlayTransform transform);

    WristOverlayPlacementReadback ReadPlacement();
}

public sealed record WristOverlayPlacementReadback(
    OverlayPlacementMode PlacementMode,
    VrHand? DockHand,
    WristOverlayTrackingOrigin? TrackingOrigin,
    OpenVrMatrix34 Transform);
