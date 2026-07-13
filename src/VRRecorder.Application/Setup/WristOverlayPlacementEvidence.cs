using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Setup;

public sealed record WristOverlayPlacementEvidence(
    OverlayPlacementMode PlacementMode,
    OverlayTransform AppliedTransform,
    bool IsVisible,
    bool IsReadableConfirmed,
    bool IsInteractionUnobstructedConfirmed);
