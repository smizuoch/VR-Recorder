using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristUiSessionTests
{
    [Fact]
    public async Task NavigatesAndRoutesSmallNudgesWithoutChangingRecorderRevision()
    {
        var application = new CapturingCommands();
        var placement = new CapturingPlacementCommands();
        var session = new WristUiSession(
            EnglishUiLocalizer.Instance,
            application,
            placement);
        var status = new RecorderStatusSnapshot(
            Revision: 9,
            RecorderState.Ready,
            RecorderAvailableActions.Start);

        var main = session.Project(status);
        await session.DispatchAsync(
            UiCommandId.OpenOverlayPositioning,
            UiActivationKind.WristRay,
            CancellationToken.None);
        var positioning = session.Project(status);
        await session.DispatchAsync(
            UiCommandId.NudgeOverlayLeft,
            UiActivationKind.WristRay,
            CancellationToken.None);
        var nudged = session.Project(status);
        await session.DispatchAsync(
            UiCommandId.PinOverlayInWorld,
            UiActivationKind.WristRay,
            CancellationToken.None);
        await session.DispatchAsync(
            UiCommandId.DockOverlayToWrist,
            UiActivationKind.WristRay,
            CancellationToken.None);
        await session.DispatchAsync(
            UiCommandId.RecenterOverlay,
            UiActivationKind.WristRay,
            CancellationToken.None);
        await session.DispatchAsync(
            UiCommandId.CloseOverlayPositioning,
            UiActivationKind.WristRay,
            CancellationToken.None);
        var returned = session.Project(status);

        Assert.Equal(WristPage.Main, main.Page);
        Assert.Equal(0, main.PresentationRevision);
        Assert.Equal(WristPage.Positioning, positioning.Page);
        Assert.Equal(1, positioning.PresentationRevision);
        Assert.Equal(9, positioning.Revision);
        Assert.Equal(
            [
                (
                    WristOverlayNudgeDirection.Left,
                    WristOverlayNudgeSize.Small),
            ],
            placement.Nudges);
        Assert.Equal(
            [
                OverlayPlacementMode.WorldPin,
                OverlayPlacementMode.WristDock,
            ],
            placement.PlacementModes);
        Assert.Equal(2, nudged.PresentationRevision);
        Assert.Equal(1, placement.RecenterCount);
        Assert.Equal(WristPage.Main, returned.Page);
        Assert.Equal(6, returned.PresentationRevision);
        Assert.Empty(application.Commands);
    }

    [Fact]
    public async Task ForwardsRecordingCommandsToTheApplicationDispatcher()
    {
        var application = new CapturingCommands();
        var session = new WristUiSession(
            EnglishUiLocalizer.Instance,
            application,
            new CapturingPlacementCommands());

        await session.DispatchAsync(
            UiCommandId.ToggleRecording,
            UiActivationKind.WristRay,
            CancellationToken.None);

        Assert.Equal(
            [(UiCommandId.ToggleRecording, UiActivationKind.WristRay)],
            application.Commands);
    }

    private sealed class CapturingCommands : IUiCommandDispatcher
    {
        public List<(UiCommandId Command, UiActivationKind Activation)>
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

    private sealed class CapturingPlacementCommands
        : IWristOverlayAdjustmentCommands
    {
        public List<(
            WristOverlayNudgeDirection Direction,
            WristOverlayNudgeSize Size)> Nudges
        { get; } = [];

        public int RecenterCount { get; private set; }

        public List<OverlayPlacementMode> PlacementModes { get; } = [];

        public Task<VrOverlayPlacement> NudgeAsync(
            WristOverlayNudgeDirection direction,
            WristOverlayNudgeSize size,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Nudges.Add((direction, size));
            return Task.FromResult(DefaultPlacement());
        }

        public Task<VrOverlayPlacement> RecenterAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecenterCount++;
            return Task.FromResult(DefaultPlacement());
        }

        public Task<VrOverlayPlacement> SetPlacementModeAsync(
            OverlayPlacementMode placementMode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlacementModes.Add(placementMode);
            return Task.FromResult(DefaultPlacement() with
            {
                PlacementMode = placementMode,
            });
        }

        public Task<VrOverlayPlacement> DragReleaseAsync(
            WristOverlayDragDelta delta,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DefaultPlacement());
        }

        private static VrOverlayPlacement DefaultPlacement() => new(
            OverlayPlacementMode.WristDock,
            WristOverlayPoseContract.CreateDefaultWristDockTransform());
    }
}
