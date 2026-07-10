namespace VRRecorder.Application.Camera;

public sealed record CameraRestoreWarning(
    CameraRestoreWarningReason Reason,
    Exception Failure);
