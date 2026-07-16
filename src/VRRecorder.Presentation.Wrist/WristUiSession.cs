using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristUiSession
    : IWristUiSnapshotProjector,
      IUiCommandDispatcher,
      IWristOverlayDragDispatcher,
      IDisposable
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly WristUiProjector _projector;
    private readonly IUiCommandDispatcher _application;
    private readonly IWristOverlayAdjustmentCommands _placement;
    private WristPage _page = WristPage.Main;
    private long _presentationRevision;
    private bool _disposed;

    public WristUiSession(
        IUiLocalizer localizer,
        IUiCommandDispatcher application,
        IWristOverlayAdjustmentCommands placement)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(placement);
        _projector = new WristUiProjector(localizer);
        _application = application;
        _placement = placement;
    }

    public WristUiSnapshot Project(RecorderStatusSnapshot status)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WristPage page;
        long presentationRevision;
        lock (_stateGate)
        {
            page = _page;
            presentationRevision = _presentationRevision;
        }
        return _projector.Project(status, page) with
        {
            PresentationRevision = presentationRevision,
        };
    }

    public async Task DispatchAsync(
        UiCommandId command,
        UiActivationKind activationKind,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _commandGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            switch (command)
            {
                case UiCommandId.OpenOverlayPositioning:
                    SetPage(WristPage.Positioning);
                    break;
                case UiCommandId.CloseOverlayPositioning:
                    SetPage(WristPage.Main);
                    break;
                case UiCommandId.DockOverlayToWrist:
                    await SetPlacementModeAsync(
                            OverlayPlacementMode.WristDock,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UiCommandId.PinOverlayInWorld:
                    await SetPlacementModeAsync(
                            OverlayPlacementMode.WorldPin,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UiCommandId.NudgeOverlayUp:
                    await NudgeAsync(
                            WristOverlayNudgeDirection.Up,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UiCommandId.NudgeOverlayDown:
                    await NudgeAsync(
                            WristOverlayNudgeDirection.Down,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UiCommandId.NudgeOverlayLeft:
                    await NudgeAsync(
                            WristOverlayNudgeDirection.Left,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UiCommandId.NudgeOverlayRight:
                    await NudgeAsync(
                            WristOverlayNudgeDirection.Right,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case UiCommandId.RecenterOverlay:
                    await _placement
                        .RecenterAsync(cancellationToken)
                        .ConfigureAwait(false);
                    IncrementPresentationRevision();
                    break;
                default:
                    await _application
                        .DispatchAsync(
                            command,
                            activationKind,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task ReleaseDragAsync(
        WristOverlayDragDelta delta,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _commandGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _placement
                .DragReleaseAsync(delta, cancellationToken)
                .ConfigureAwait(false);
            IncrementPresentationRevision();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _commandGate.Dispose();
    }

    private async Task NudgeAsync(
        WristOverlayNudgeDirection direction,
        CancellationToken cancellationToken)
    {
        await _placement
            .NudgeAsync(
                direction,
                WristOverlayNudgeSize.Small,
                cancellationToken)
            .ConfigureAwait(false);
        IncrementPresentationRevision();
    }

    private async Task SetPlacementModeAsync(
        OverlayPlacementMode placementMode,
        CancellationToken cancellationToken)
    {
        await _placement
            .SetPlacementModeAsync(placementMode, cancellationToken)
            .ConfigureAwait(false);
        IncrementPresentationRevision();
    }

    private void SetPage(WristPage page)
    {
        lock (_stateGate)
        {
            if (_page == page)
            {
                return;
            }
            _page = page;
            _presentationRevision++;
        }
    }

    private void IncrementPresentationRevision()
    {
        lock (_stateGate)
        {
            _presentationRevision++;
        }
    }
}
