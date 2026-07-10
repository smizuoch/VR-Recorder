namespace VRRecorder.Domain.Recording;

public static class RecorderStateMachine
{
    public static RecorderState Transition(RecorderState state, RecorderTrigger trigger) =>
        (state, trigger) switch
        {
            (RecorderState.Ready, RecorderTrigger.StartRequested) => RecorderState.Arming,
            _ => throw new InvalidOperationException(
                $"Transition from {state} by {trigger} is not defined."),
        };
}
