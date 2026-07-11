using VRRecorder.Application.Desktop;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopTrayUiControllerTests
{
    [Theory]
    [InlineData(
        RecorderState.Booting,
        DesktopTrayState.Warning,
        "Recording_Start_Short",
        false)]
    [InlineData(
        RecorderState.Ready,
        DesktopTrayState.Ready,
        "Recording_Start_Short",
        true)]
    [InlineData(
        RecorderState.Arming,
        DesktopTrayState.Warning,
        "Recording_Action_Cancel_Short",
        true)]
    [InlineData(
        RecorderState.Countdown,
        DesktopTrayState.Warning,
        "Recording_Action_Cancel_Short",
        true)]
    [InlineData(
        RecorderState.Starting,
        DesktopTrayState.Warning,
        "Recording_Start_Short",
        false)]
    [InlineData(
        RecorderState.Recording,
        DesktopTrayState.Recording,
        "Recording_Stop_Short",
        true)]
    [InlineData(
        RecorderState.SignalLost,
        DesktopTrayState.Warning,
        "Recording_Stop_Short",
        true)]
    [InlineData(
        RecorderState.Stopping,
        DesktopTrayState.Warning,
        "Recording_Stop_Short",
        false)]
    [InlineData(
        RecorderState.NoSignal,
        DesktopTrayState.Warning,
        "Recording_Action_Retry_Short",
        true)]
    [InlineData(
        RecorderState.Faulted,
        DesktopTrayState.Fault,
        "Recording_Start_Short",
        false)]
    [InlineData(
        RecorderState.ComplianceFault,
        DesktopTrayState.Fault,
        "Recording_Start_Short",
        false)]
    public void ProjectsEveryRecorderStateToFourStateTrayContract(
        RecorderState recorderState,
        DesktopTrayState expectedTrayState,
        string expectedActionResource,
        bool expectedActionEnabled)
    {
        var controller = new DesktopTrayUiController();

        var update = controller.Apply(
            RecorderStatusSnapshot.Create(3, recorderState));

        Assert.NotNull(update);
        Assert.Equal(3, update.Revision);
        Assert.Equal(expectedTrayState, update.State);
        Assert.Equal(
            $"Tray_State_{expectedTrayState}",
            update.StateLabelResourceKey);
        Assert.Equal(expectedActionResource, update.ActionLabelResourceKey);
        Assert.Equal(expectedActionEnabled, update.IsActionEnabled);
    }

    [Fact]
    public void DuplicateOrTerminallyDelayedUpdatesCannotRegressTrayState()
    {
        var controller = new DesktopTrayUiController();
        var fault = controller.Apply(
            RecorderStatusSnapshot.Create(8, RecorderState.Faulted));

        var duplicate = controller.Apply(
            RecorderStatusSnapshot.Create(8, RecorderState.Recording));
        var delayed = controller.Apply(
            RecorderStatusSnapshot.Create(9, RecorderState.Ready));

        Assert.NotNull(fault);
        Assert.Null(duplicate);
        Assert.Null(delayed);
        Assert.Same(fault, controller.Current);
    }
}
