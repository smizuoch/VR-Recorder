using VRRecorder.Application.Audio;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Presentation;

public sealed record RecorderStatusSnapshot(
    long Revision,
    RecorderState State,
    RecorderAvailableActions AvailableActions,
    RecordingAudioControlState? AudioControlState = null)
{
    public static RecorderStatusSnapshot Create(
        long revision,
        RecorderState state,
        RecordingAudioControlState? audioControlState = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown recorder state.");
        }

        var actions = state switch
        {
            RecorderState.Ready => RecorderAvailableActions.Start,
            RecorderState.Arming or RecorderState.Countdown =>
                RecorderAvailableActions.Cancel,
            RecorderState.Recording or RecorderState.SignalLost =>
                RecorderAvailableActions.Stop,
            RecorderState.NoSignal => RecorderAvailableActions.Retry,
            _ => RecorderAvailableActions.None,
        };
        return new RecorderStatusSnapshot(
            revision,
            state,
            actions,
            audioControlState);
    }
}
