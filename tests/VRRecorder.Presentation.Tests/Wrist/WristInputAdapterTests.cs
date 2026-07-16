using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristInputAdapterTests
{
    [Fact]
    public async Task RecordingRayActivationUsesCanonicalSharedDispatcher()
    {
        var action = Assert.Single(new WristUiProjector(
                EnglishUiLocalizer.Instance)
            .Project(new RecorderStatusSnapshot(
                Revision: 1,
                State: RecorderState.Ready,
                AvailableActions: RecorderAvailableActions.Start))
            .Actions,
            item => item.Command == UiCommandId.ToggleRecording);
        var commands = new CapturingUiCommandDispatcher();
        var adapter = new WristInputAdapter(commands);

        await adapter.ActivateAsync(action, CancellationToken.None);

        var dispatched = Assert.Single(commands.Commands);
        Assert.Equal(UiCommandId.ToggleRecording, dispatched.Command);
        Assert.Equal(UiActivationKind.WristRay, dispatched.ActivationKind);
    }

    [Fact]
    public async Task DisabledRayTargetDispatchesNothing()
    {
        var action = new UiActionSnapshot(
            SemanticId: "recording.start",
            Command: UiCommandId.ToggleRecording,
            IconSemanticId: "recording.start",
            ComponentRole: UiComponentRole.LargeFilledIconButton,
            ColorRole: UiColorRole.Recording,
            IsEnabled: false,
            VisibleLabel: new LocalizedText("recording.start.short", "REC"),
            AccessibleName: new LocalizedText(
                "recording.start.accessible",
                "Start recording"),
            Tooltip: new LocalizedText(
                "recording.start.accessible",
                "Start recording"),
            MinimumTargetDp: 56);
        var commands = new CapturingUiCommandDispatcher();
        var adapter = new WristInputAdapter(commands);

        await adapter.ActivateAsync(action, CancellationToken.None);

        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task HitTestsRayCoordinatesAndDispatchesTheSnapshotAction()
    {
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(new RecorderStatusSnapshot(
                Revision: 2,
                State: RecorderState.Ready,
                AvailableActions: RecorderAvailableActions.Start));
        var layout = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);
        var target = Assert.Single(layout.HitTargets, item =>
            item.Command == UiCommandId.ToggleRecording);
        var commands = new CapturingUiCommandDispatcher();
        var adapter = new WristInputAdapter(commands);

        var handled = await adapter.ActivateAtAsync(
            snapshot,
            layout,
            target.Bounds.Left + target.Bounds.Width / 2,
            target.Bounds.Top + target.Bounds.Height / 2,
            CancellationToken.None);

        Assert.True(handled);
        var dispatched = Assert.Single(commands.Commands);
        Assert.Equal(UiCommandId.ToggleRecording, dispatched.Command);
        Assert.Equal(UiActivationKind.WristRay, dispatched.ActivationKind);
    }

    [Fact]
    public async Task IgnoresMissesAndTargetsFromAStaleSnapshot()
    {
        var projector = new WristUiProjector(EnglishUiLocalizer.Instance);
        var actionable = projector.Project(new RecorderStatusSnapshot(
            Revision: 3,
            State: RecorderState.Ready,
            AvailableActions: RecorderAvailableActions.Start));
        var layout = WristTextureLayoutEngine.Layout(
            actionable,
            WristLayoutOptions.Default);
        var target = Assert.Single(layout.HitTargets, item =>
            item.Command == UiCommandId.ToggleRecording);
        var stale = projector.Project(new RecorderStatusSnapshot(
            Revision: 4,
            State: RecorderState.Recording,
            AvailableActions: RecorderAvailableActions.Stop));
        var commands = new CapturingUiCommandDispatcher();
        var adapter = new WristInputAdapter(commands);

        Assert.False(await adapter.ActivateAtAsync(
            actionable,
            layout,
            0,
            0,
            CancellationToken.None));
        Assert.False(await adapter.ActivateAtAsync(
            stale,
            layout,
            target.Bounds.Left + target.Bounds.Width / 2,
            target.Bounds.Top + target.Bounds.Height / 2,
            CancellationToken.None));
        Assert.Empty(commands.Commands);
    }

    private sealed class CapturingUiCommandDispatcher : IUiCommandDispatcher
    {
        public List<(UiCommandId Command, UiActivationKind ActivationKind)>
            Commands
        { get; } = [];

        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add((command, activationKind));
            return Task.CompletedTask;
        }
    }
}
