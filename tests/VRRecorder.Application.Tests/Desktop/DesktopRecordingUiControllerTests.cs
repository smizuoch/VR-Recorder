using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingUiControllerTests
{
    [Theory]
    [InlineData(
        RecorderState.Ready,
        "Recording_Start_Short",
        "Recording_Start_AccessibleName",
        true)]
    [InlineData(
        RecorderState.Arming,
        "Recording_Action_Cancel_Short",
        "Recording_Action_Cancel_AccessibleName",
        true)]
    [InlineData(
        RecorderState.Countdown,
        "Recording_Action_Cancel_Short",
        "Recording_Action_Cancel_AccessibleName",
        true)]
    [InlineData(
        RecorderState.Starting,
        "Recording_Start_Short",
        "Recording_Start_AccessibleName",
        false)]
    [InlineData(
        RecorderState.Recording,
        "Recording_Stop_Short",
        "Recording_Stop_AccessibleName",
        true)]
    [InlineData(
        RecorderState.SignalLost,
        "Recording_Stop_Short",
        "Recording_Stop_AccessibleName",
        true)]
    [InlineData(
        RecorderState.Stopping,
        "Recording_Stop_Short",
        "Recording_Stop_AccessibleName",
        false)]
    [InlineData(
        RecorderState.NoSignal,
        "Recording_Action_Retry_Short",
        "Recording_Action_Retry_AccessibleName",
        true)]
    [InlineData(
        RecorderState.Faulted,
        "Recording_Start_Short",
        "Recording_Start_AccessibleName",
        false)]
    [InlineData(
        RecorderState.ComplianceFault,
        "Recording_Start_Short",
        "Recording_Start_AccessibleName",
        false)]
    public void ProjectsLiveStateToLocalizedAccessiblePrimaryAction(
        RecorderState state,
        string expectedLabel,
        string expectedAccessibleName,
        bool expectedEnabled)
    {
        var controller = new DesktopRecordingUiController();

        var update = controller.Apply(
            RecorderStatusSnapshot.Create(1, state));

        Assert.NotNull(update);
        Assert.Equal(state, update.State);
        Assert.Equal($"Recording_State_{state}", update.StatusTextResourceKey);
        Assert.Equal(
            $"Status_{state}_AccessibleDescription",
            update.StatusAccessibleDescriptionResourceKey);
        Assert.Equal(expectedLabel, update.ActionLabelResourceKey);
        Assert.Equal(expectedAccessibleName, update.ActionAccessibleNameResourceKey);
        Assert.Equal(
            expectedAccessibleName.Replace("AccessibleName", "Tooltip", StringComparison.Ordinal),
            update.ActionHelpResourceKey);
        Assert.Equal(expectedEnabled, update.IsActionEnabled);
    }

    [Fact]
    public void DuplicateAndOutOfOrderUpdatesCannotRegressVisibleState()
    {
        var controller = new DesktopRecordingUiController();

        var recording = controller.Apply(
            RecorderStatusSnapshot.Create(8, RecorderState.Recording));
        var duplicate = controller.Apply(
            RecorderStatusSnapshot.Create(8, RecorderState.SignalLost));
        var stale = controller.Apply(
            RecorderStatusSnapshot.Create(7, RecorderState.Ready));

        Assert.NotNull(recording);
        Assert.Null(duplicate);
        Assert.Null(stale);
        Assert.Same(recording, controller.Current);
    }

    [Theory]
    [InlineData(RecorderState.Faulted)]
    [InlineData(RecorderState.ComplianceFault)]
    public void TerminalStateRejectsEvenHigherDelayedRuntimeUpdate(
        RecorderState terminalState)
    {
        var controller = new DesktopRecordingUiController();
        var terminal = controller.Apply(
            RecorderStatusSnapshot.Create(10, terminalState));

        var delayed = controller.Apply(
            RecorderStatusSnapshot.Create(11, RecorderState.Ready));

        Assert.NotNull(terminal);
        Assert.Null(delayed);
        Assert.Same(terminal, controller.Current);
    }
}
