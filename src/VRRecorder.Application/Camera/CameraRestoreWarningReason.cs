namespace VRRecorder.Application.Camera;

public enum CameraRestoreWarningReason
{
    RecordingCompleted,
    StartCanceled,
    NoSignal,
    InsufficientStorage,
    StartFailed,
    StaleLeaseRecovery,
}
