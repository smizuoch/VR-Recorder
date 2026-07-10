namespace VRRecorder.Domain.Recording;

public enum RecorderTrigger
{
    LegalVerificationSucceeded,
    LegalVerificationFailed,
    StartRequested,
    SignalTimeout,
    FirstPacketCommitted,
    DurationElapsed,
    StopRequested,
    FreshFrameTimeout,
    SignalRecovered,
    GraceExpired,
    StopCompleted,
}
