using VRRecorder.Application.Settings;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Ports;

public interface IWristOverlayPlacementVerifier
{
    Task<WristOverlayPlacementEvidence?> VerifyAsync(
        VrSettings settings,
        CancellationToken cancellationToken);
}
