using VRRecorder.Domain.Recording;

namespace VRRecorder.Domain.Tests.Recording;

public sealed class RecorderStateMachineTests
{
    [Fact]
    public void StartRequested_WhenReady_TransitionsToArming()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Ready,
            RecorderTrigger.StartRequested);

        Assert.Equal(RecorderState.Arming, next);
    }
}
