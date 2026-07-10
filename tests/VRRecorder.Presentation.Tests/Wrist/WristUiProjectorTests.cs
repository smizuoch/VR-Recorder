using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristUiProjectorTests
{
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
