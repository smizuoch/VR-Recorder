using VRRecorder.Domain.Recording;

namespace VRRecorder.Domain.Tests.Recording;

public sealed class RecorderStateMachineTests
{
    [Fact]
    public void StartRequestedWhenReadyTransitionsToArming()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Ready,
            RecorderTrigger.StartRequested);

        Assert.Equal(RecorderState.Arming, next);
    }

    [Fact]
    public void StartRequestedWhenArmingKeepsArming()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Arming,
            RecorderTrigger.StartRequested);

        Assert.Equal(RecorderState.Arming, next);
    }

    [Fact]
    public void SignalTimeoutWhenArmingTransitionsToNoSignal()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Arming,
            RecorderTrigger.SignalTimeout);

        Assert.Equal(RecorderState.NoSignal, next);
    }

    [Fact]
    public void FirstPacketCommittedWhenStartingTransitionsToRecording()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Starting,
            RecorderTrigger.FirstPacketCommitted);

        Assert.Equal(RecorderState.Recording, next);
    }

    [Fact]
    public void DurationElapsedWhenRecordingTransitionsToStopping()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Recording,
            RecorderTrigger.DurationElapsed);

        Assert.Equal(RecorderState.Stopping, next);
    }
}
