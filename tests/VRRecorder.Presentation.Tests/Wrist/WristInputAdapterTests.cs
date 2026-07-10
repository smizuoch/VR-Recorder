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
            .Actions);
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
