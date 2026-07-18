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
    private readonly IUiLocalizer _localizer;
    private readonly IUiCommandDispatcher _application;
    private readonly IWristOverlayAdjustmentCommands _placement;
    private readonly IWristTelemetrySource _telemetry;
    private WristPage _page = WristPage.Main;
    private OverlayPlacementMode _placementMode;
    private long _presentationRevision;
    private bool _disposed;

    public WristUiSession(
        IUiLocalizer localizer,
        IUiCommandDispatcher application,
        IWristOverlayAdjustmentCommands placement)
        : this(
            localizer,
            application,
            placement,
            NullWristTelemetrySource.Instance,
            OverlayPlacementMode.WristDock)
    {
    }

    public WristUiSession(
        IUiLocalizer localizer,
        IUiCommandDispatcher application,
        IWristOverlayAdjustmentCommands placement,
        IWristTelemetrySource telemetry,
        OverlayPlacementMode initialPlacementMode)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (!Enum.IsDefined(initialPlacementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialPlacementMode));
        }
        _localizer = localizer;
        _projector = new WristUiProjector(localizer);
        _application = application;
        _placement = placement;
        _telemetry = telemetry;
        _placementMode = initialPlacementMode;
    }

    public WristUiSnapshot Project(RecorderStatusSnapshot status)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WristPage page;
        long presentationRevision;
        OverlayPlacementMode placementMode;
        lock (_stateGate)
        {
            page = _page;
            presentationRevision = _presentationRevision;
            placementMode = _placementMode;
        }
        var telemetry = _telemetry.Capture(
            status,
            placementMode,
            _localizer);
        return _projector.Project(status, page, telemetry) with
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
                    CommitPlacement(await _placement
                        .RecenterAsync(cancellationToken)
                        .ConfigureAwait(false));
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
            CommitPlacement(await _placement
                .DragReleaseAsync(delta, cancellationToken)
                .ConfigureAwait(false));
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
        CommitPlacement(await _placement
            .NudgeAsync(
                direction,
                WristOverlayNudgeSize.Small,
                cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task SetPlacementModeAsync(
        OverlayPlacementMode placementMode,
        CancellationToken cancellationToken)
    {
        CommitPlacement(await _placement
            .SetPlacementModeAsync(placementMode, cancellationToken)
            .ConfigureAwait(false));
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

    private void CommitPlacement(VrOverlayPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!Enum.IsDefined(placement.PlacementMode))
        {
            throw new InvalidDataException(
                "The applied wrist placement mode is invalid.");
        }
        lock (_stateGate)
        {
            _placementMode = placement.PlacementMode;
            _presentationRevision++;
        }
    }
}
