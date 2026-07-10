using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristUiProjector
{
    private readonly IUiLocalizer _localizer;

    public WristUiProjector(IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
    }

    public WristUiSnapshot Project(RecorderStatusSnapshot status) =>
        Project(status, WristPage.Main);

    public WristUiSnapshot Project(
        RecorderStatusSnapshot status,
        WristPage page)
    {
        ArgumentNullException.ThrowIfNull(status);
        UiActionSnapshot[] actions;
        if (status.State == RecorderState.Recording &&
            status.AvailableActions.HasFlag(RecorderAvailableActions.Stop))
        {
            actions = [CreateStopAction()];
        }
        else if (status.State == RecorderState.NoSignal &&
                 status.AvailableActions.HasFlag(RecorderAvailableActions.Retry))
        {
            actions = [CreateRetryAction()];
        }
        else if (status.State == RecorderState.Ready &&
                 status.AvailableActions.HasFlag(RecorderAvailableActions.Start))
        {
            actions = [CreateStartAction()];
        }
        else
        {
            actions = [];
        }

        return new WristUiSnapshot(
            status.Revision,
            status.State,
            page,
            actions);
    }

    private UiActionSnapshot CreateStartAction()
    {
        var accessibleName = _localizer.Resolve("recording.start.accessible");
        return new UiActionSnapshot(
            SemanticId: "recording.start",
            Command: UiCommandId.StartRecording,
            IconSemanticId: "recording.start",
            ComponentRole: UiComponentRole.LargeFilledIconButton,
            ColorRole: UiColorRole.Recording,
            IsEnabled: true,
            VisibleLabel: _localizer.Resolve("recording.start.short"),
            AccessibleName: accessibleName,
            Tooltip: accessibleName,
            MinimumTargetDp: 56);
    }

    private UiActionSnapshot CreateStopAction()
    {
        var accessibleName = _localizer.Resolve("recording.stop.accessible");
        return new UiActionSnapshot(
            SemanticId: "recording.stop",
            Command: UiCommandId.StopRecording,
            IconSemanticId: "recording.stop",
            ComponentRole: UiComponentRole.LargeFilledIconButton,
            ColorRole: UiColorRole.Recording,
            IsEnabled: true,
            VisibleLabel: _localizer.Resolve("recording.stop.short"),
            AccessibleName: accessibleName,
            Tooltip: accessibleName,
            MinimumTargetDp: 64);
    }

    private UiActionSnapshot CreateRetryAction()
    {
        var accessibleName = _localizer.Resolve("camera.retry.accessible");
        return new UiActionSnapshot(
            SemanticId: "camera.retry",
            Command: UiCommandId.Retry,
            IconSemanticId: "camera.retry",
            ComponentRole: UiComponentRole.FilledTonalButton,
            ColorRole: UiColorRole.Error,
            IsEnabled: true,
            VisibleLabel: _localizer.Resolve("camera.retry.short"),
            AccessibleName: accessibleName,
            Tooltip: accessibleName,
            MinimumTargetDp: 56);
    }
}
