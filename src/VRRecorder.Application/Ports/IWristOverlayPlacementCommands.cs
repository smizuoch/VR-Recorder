using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Ports;

public interface IWristOverlayPlacementCommands
{
    Task<VrOverlayPlacement> RecenterAsync(
        CancellationToken cancellationToken);
}
