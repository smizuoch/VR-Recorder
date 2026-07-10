using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristUiProjectorTests
{
    [Fact]
    public void NoSignalSuppressesStartAndProjectsAccessibleRetry()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 3,
            State: RecorderState.NoSignal,
            AvailableActions:
                RecorderAvailableActions.Start | RecorderAvailableActions.Retry);

        var snapshot = projector.Project(status);

        Assert.DoesNotContain(snapshot.Actions, action =>
            action.Command == UiCommandId.StartRecording);
        var retry = Assert.Single(snapshot.Actions);
        Assert.Equal(UiCommandId.Retry, retry.Command);
        Assert.Equal(UiComponentRole.FilledTonalButton, retry.ComponentRole);
        Assert.Equal(UiColorRole.Error, retry.ColorRole);
        Assert.Equal("RETRY", retry.VisibleLabel.Value);
        Assert.Equal("Retry camera connection", retry.AccessibleName.Value);
    }

    [Theory]
    [InlineData(RecorderState.Faulted)]
    [InlineData(RecorderState.ComplianceFault)]
    public void FaultStatesNeverProjectStart(RecorderState state)
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 4,
            State: state,
            AvailableActions: RecorderAvailableActions.Start);

        var snapshot = projector.Project(status);

        Assert.DoesNotContain(snapshot.Actions, action =>
            action.Command == UiCommandId.StartRecording);
    }

    [Theory]
    [InlineData(WristPage.Main)]
    [InlineData(WristPage.Settings)]
    [InlineData(WristPage.Legal)]
    [InlineData(WristPage.Positioning)]
    public void RecordingKeepsCriticalStopReachableFromEveryPage(WristPage page)
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 2,
            State: RecorderState.Recording,
            AvailableActions: RecorderAvailableActions.Stop);

        var snapshot = projector.Project(status, page);

        var action = Assert.Single(snapshot.Actions, item =>
            item.SemanticId == "recording.stop");
        Assert.Equal(UiCommandId.StopRecording, action.Command);
        Assert.True(action.IsEnabled);
        Assert.Equal("STOP", action.VisibleLabel.Value);
        Assert.Equal("Stop recording", action.AccessibleName.Value);
        Assert.Equal("Stop recording", action.Tooltip.Value);
        Assert.True(action.MinimumTargetDp >= 64);
        Assert.Equal(page, snapshot.Page);
    }

    [Fact]
    public void ReadyProjectsOneEnabledAccessibleRecordAction()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var status = new RecorderStatusSnapshot(
            Revision: 1,
            State: RecorderState.Ready,
            AvailableActions: RecorderAvailableActions.Start);

        var snapshot = projector.Project(status);

        var action = Assert.Single(snapshot.Actions, item =>
            item.SemanticId == "recording.start");
        Assert.Equal(UiCommandId.StartRecording, action.Command);
        Assert.Equal("recording.start", action.IconSemanticId);
        Assert.Equal(
            UiComponentRole.LargeFilledIconButton,
            action.ComponentRole);
        Assert.Equal(UiColorRole.Recording, action.ColorRole);
        Assert.True(action.IsEnabled);
        Assert.Equal("REC", action.VisibleLabel.Value);
        Assert.Equal("Start recording", action.AccessibleName.Value);
        Assert.Equal("Start recording", action.Tooltip.Value);
        Assert.True(action.MinimumTargetDp >= 56);
    }
}
