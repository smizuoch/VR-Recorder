namespace VRRecorder.Domain.Recording;

public enum RecorderTrigger
{
    LegalVerificationSucceeded,
    LegalVerificationFailed,
    RepairCompleted,
    StartRequested,
    SignalTimeout,
    FirstPacketCommitted,
    DurationElapsed,
    StopRequested,
    FreshFrameTimeout,
    SignalRecovered,
    GraceExpired,
    StopCompleted,
    CountdownStarted,
    StartPreparationCompleted,
    CancelRequested,
}
