namespace VRRecorder.Domain.Recording;

public enum RecorderState
{
    Booting,
    ComplianceFault,
    Ready,
    Arming,
    Countdown,
    Starting,
    Recording,
    SignalLost,
    Stopping,
    NoSignal,
    Faulted,
}
