namespace VRRecorder.Application.Recording;

public enum RecordingStopReason
{
    UserRequested,
    AutoStop,
    SignalLost,
    DiskLow,
    EncoderFailure,
}
