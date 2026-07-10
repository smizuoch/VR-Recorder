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

    [Fact]
    public void StopRequestedWhenRecordingTransitionsToStopping()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Recording,
            RecorderTrigger.StopRequested);

        Assert.Equal(RecorderState.Stopping, next);
    }

    [Fact]
    public void StopRequestedWhenStoppingKeepsStopping()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Stopping,
            RecorderTrigger.StopRequested);

        Assert.Equal(RecorderState.Stopping, next);
    }

    [Fact]
    public void FreshFrameTimeoutWhenRecordingTransitionsToSignalLost()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.Recording,
            RecorderTrigger.FreshFrameTimeout);

        Assert.Equal(RecorderState.SignalLost, next);
    }

    [Fact]
    public void SignalRecoveredWhenLostReturnsToRecording()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.SignalLost,
            RecorderTrigger.SignalRecovered);

        Assert.Equal(RecorderState.Recording, next);
    }

    [Fact]
    public void GraceExpiredWhenSignalLostTransitionsToStopping()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.SignalLost,
            RecorderTrigger.GraceExpired);

        Assert.Equal(RecorderState.Stopping, next);
    }
}
