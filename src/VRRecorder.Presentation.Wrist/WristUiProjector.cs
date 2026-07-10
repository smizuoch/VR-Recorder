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

    public WristUiSnapshot Project(RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        UiActionSnapshot[] actions = status.State == RecorderState.Ready &&
                                     status.AvailableActions.HasFlag(
                                         RecorderAvailableActions.Start)
            ? [CreateStartAction()]
            : [];
        return new WristUiSnapshot(status.Revision, status.State, actions);
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
}
