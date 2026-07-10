namespace VRRecorder.Domain.Recording;

public static class RecorderStateMachine
{
    public static RecorderState Transition(RecorderState state, RecorderTrigger trigger) =>
        (state, trigger) switch
        {
            (RecorderState.Ready, RecorderTrigger.StartRequested) => RecorderState.Arming,
            (RecorderState.Arming, RecorderTrigger.StartRequested) => RecorderState.Arming,
            (RecorderState.Arming, RecorderTrigger.SignalTimeout) => RecorderState.NoSignal,
            (RecorderState.Starting, RecorderTrigger.FirstPacketCommitted) =>
                RecorderState.Recording,
            (RecorderState.Recording, RecorderTrigger.DurationElapsed) =>
                RecorderState.Stopping,
            (RecorderState.Recording, RecorderTrigger.StopRequested) =>
                RecorderState.Stopping,
            (RecorderState.Stopping, RecorderTrigger.StopRequested) =>
                RecorderState.Stopping,
            (RecorderState.Recording, RecorderTrigger.FreshFrameTimeout) =>
                RecorderState.SignalLost,
            (RecorderState.SignalLost, RecorderTrigger.SignalRecovered) =>
                RecorderState.Recording,
            (RecorderState.SignalLost, RecorderTrigger.GraceExpired) =>
                RecorderState.Stopping,
            (RecorderState.Stopping, RecorderTrigger.StopCompleted) =>
                RecorderState.Ready,
            _ => throw new InvalidOperationException(
                $"Transition from {state} by {trigger} is not defined."),
        };
}
