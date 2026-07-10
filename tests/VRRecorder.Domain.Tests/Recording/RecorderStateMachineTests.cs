using VRRecorder.Domain.Recording;

namespace VRRecorder.Domain.Tests.Recording;

public sealed class RecorderStateMachineTests
{
    [Theory]
    [InlineData("LegalVerificationSucceeded", RecorderState.Ready)]
    [InlineData("LegalVerificationFailed", RecorderState.ComplianceFault)]
    public void BootOutcomeIsDeterminedByLegalBundleVerification(
        string triggerName,
        RecorderState expected)
    {
        var trigger = Enum.Parse<RecorderTrigger>(triggerName);

        var next = RecorderStateMachine.Transition(
            RecorderState.Booting,
            trigger);

        Assert.Equal(expected, next);
    }

    [Fact]
    public void CompletedComplianceRepairRestartsVerification()
    {
        var repairCompleted = Enum.Parse<RecorderTrigger>("RepairCompleted");

        var next = RecorderStateMachine.Transition(
            RecorderState.ComplianceFault,
            repairCompleted);

        Assert.Equal(RecorderState.Booting, next);
    }

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
    public void StartRequestedWhenNoSignalRetriesArming()
    {
        var next = RecorderStateMachine.Transition(
            RecorderState.NoSignal,
            RecorderTrigger.StartRequested);

        Assert.Equal(RecorderState.Arming, next);
    }

    [Theory]
    [InlineData(RecorderState.Arming, "CountdownStarted", RecorderState.Countdown)]
    [InlineData(RecorderState.Arming, "StartPreparationCompleted", RecorderState.Starting)]
    [InlineData(RecorderState.Countdown, "StartPreparationCompleted", RecorderState.Starting)]
    [InlineData(RecorderState.Arming, "CancelRequested", RecorderState.Ready)]
    [InlineData(RecorderState.Countdown, "CancelRequested", RecorderState.Ready)]
    public void StartPreparationHasExplicitLifecycleTransitions(
        RecorderState current,
        string triggerName,
        RecorderState expected)
    {
        var trigger = Enum.Parse<RecorderTrigger>(triggerName);

        var next = RecorderStateMachine.Transition(current, trigger);

        Assert.Equal(expected, next);
    }

    [Fact]
    public void CancelRequestedWhenStartingIsRejected()
    {
        var cancelRequested = Enum.Parse<RecorderTrigger>("CancelRequested");

        Assert.Throws<InvalidOperationException>(() =>
            RecorderStateMachine.Transition(
                RecorderState.Starting,
                cancelRequested));
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

    [Theory]
    [InlineData(RecorderState.Recording)]
    [InlineData(RecorderState.SignalLost)]
    public void StopRequestedFromActiveRecordingTransitionsToStopping(
        RecorderState activeState)
    {
        var next = RecorderStateMachine.Transition(
            activeState,
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

    [Fact]
    public void CompletedStopReturnsRecorderToReady()
    {
        var completed = Enum.Parse<RecorderTrigger>("StopCompleted");

        var next = RecorderStateMachine.Transition(
            RecorderState.Stopping,
            completed);

        Assert.Equal(RecorderState.Ready, next);
    }
}
