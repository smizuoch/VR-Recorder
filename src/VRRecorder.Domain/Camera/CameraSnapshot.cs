namespace VRRecorder.Domain.Camera;

public sealed record CameraSnapshot(
    ObservedCameraValue<CameraMode> Mode,
    ObservedCameraValue<bool> Streaming);
