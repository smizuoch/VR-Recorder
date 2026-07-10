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
        if ((status.State == RecorderState.Recording ||
             status.State == RecorderState.SignalLost) &&
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
            CreateStateCue(status.State),
            page,
            actions);
    }

    private UiActionSnapshot CreateStartAction()
    {
        var accessibleName = _localizer.Resolve("recording.start.accessible");
        return new UiActionSnapshot(
            SemanticId: "recording.start",
            Command: UiCommandId.ToggleRecording,
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
            Command: UiCommandId.ToggleRecording,
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

    private UiStateCue CreateStateCue(RecorderState state)
    {
        var descriptor = state switch
        {
            RecorderState.Booting =>
                (UiColorRole.Surface, "system.booting", "state.booting.label"),
            RecorderState.ComplianceFault =>
                (UiColorRole.Error, "legal.error", "state.compliance-fault.label"),
            RecorderState.Ready =>
                (UiColorRole.Surface, "recording.ready", "state.ready.label"),
            RecorderState.Arming =>
                (UiColorRole.Surface, "camera.arming", "state.arming.label"),
            RecorderState.Countdown =>
                (UiColorRole.Surface, "recording.countdown", "state.countdown.label"),
            RecorderState.Starting =>
                (UiColorRole.Surface, "recording.starting", "state.starting.label"),
            RecorderState.Recording =>
                (UiColorRole.Recording, "recording.active", "state.recording.label"),
            RecorderState.SignalLost =>
                (UiColorRole.Error, "camera.signal-lost", "state.signal-lost.label"),
            RecorderState.Stopping =>
                (UiColorRole.Surface, "recording.stopping", "state.stopping.label"),
            RecorderState.NoSignal =>
                (UiColorRole.Error, "camera.no-signal", "state.no-signal.label"),
            RecorderState.Faulted =>
                (UiColorRole.Error, "system.error", "state.faulted.label"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown recorder state."),
        };
        return new UiStateCue(
            descriptor.Item1,
            descriptor.Item2,
            _localizer.Resolve(descriptor.Item3));
    }
}
