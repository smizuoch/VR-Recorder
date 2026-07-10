namespace VRRecorder.Domain.Recording;

public static class RecorderStateMachine
{
    public static RecorderState Transition(RecorderState state, RecorderTrigger trigger) =>
        (state, trigger) switch
        {
            (RecorderState.Booting, RecorderTrigger.LegalVerificationSucceeded) =>
                RecorderState.Ready,
            (RecorderState.Booting, RecorderTrigger.LegalVerificationFailed) =>
                RecorderState.ComplianceFault,
            (RecorderState.ComplianceFault, RecorderTrigger.RepairCompleted) =>
                RecorderState.Booting,
            (RecorderState.Ready, RecorderTrigger.StartRequested) => RecorderState.Arming,
            (RecorderState.Arming, RecorderTrigger.StartRequested) => RecorderState.Arming,
            (RecorderState.Arming, RecorderTrigger.SignalTimeout) => RecorderState.NoSignal,
            (RecorderState.Arming, RecorderTrigger.CountdownStarted) =>
                RecorderState.Countdown,
            (RecorderState.Arming, RecorderTrigger.StartPreparationCompleted) =>
                RecorderState.Starting,
            (RecorderState.Countdown, RecorderTrigger.StartPreparationCompleted) =>
                RecorderState.Starting,
            (RecorderState.Arming, RecorderTrigger.CancelRequested) =>
                RecorderState.Ready,
            (RecorderState.Countdown, RecorderTrigger.CancelRequested) =>
                RecorderState.Ready,
            (RecorderState.NoSignal, RecorderTrigger.StartRequested) =>
                RecorderState.Arming,
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
