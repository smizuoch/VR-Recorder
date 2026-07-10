namespace VRRecorder.Domain.Recording;

public enum RecorderTrigger
{
    StartRequested,
    SignalTimeout,
    FirstPacketCommitted,
    DurationElapsed,
    StopRequested,
    FreshFrameTimeout,
}
