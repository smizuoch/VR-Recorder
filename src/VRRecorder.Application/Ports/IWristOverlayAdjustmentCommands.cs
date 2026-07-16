using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Ports;

public interface IWristOverlayAdjustmentCommands
{
    Task<VrOverlayPlacement> NudgeAsync(
        WristOverlayNudgeDirection direction,
        WristOverlayNudgeSize size,
        CancellationToken cancellationToken);

    Task<VrOverlayPlacement> RecenterAsync(
        CancellationToken cancellationToken);

    Task<VrOverlayPlacement> SetPlacementModeAsync(
        OverlayPlacementMode placementMode,
        CancellationToken cancellationToken);

    Task<VrOverlayPlacement> DragReleaseAsync(
        WristOverlayDragDelta delta,
        CancellationToken cancellationToken);
}
