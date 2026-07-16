using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Ports;

public interface IWristOverlayPlacementVerificationRuntime
{
    void Show();

    WristOverlayPlacementReadback ReadPlacement();
}
