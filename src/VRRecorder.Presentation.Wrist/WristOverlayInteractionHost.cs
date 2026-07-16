using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed record WristOverlayInteractionTickResult(
    WristTextureHostTickResult Texture,
    int PointerEventsPolled,
    bool ActionDispatched);

public interface IWristOverlayInteractionTicker
{
    Task<WristOverlayInteractionTickResult> TickAsync(
        WristUiSnapshot snapshot,
        TimeSpan now,
        CancellationToken cancellationToken);
}

public interface IWristOverlayDragDispatcher
{
    Task ReleaseDragAsync(
        WristOverlayDragDelta delta,
        CancellationToken cancellationToken);
}

public sealed class WristOverlayInteractionHost
    : IWristOverlayInteractionTicker
{
    public const int MaxPointerEventsPerTick = 64;
    public const int DragActivationDistancePixels = 16;
    public const double OverlayWidthMeters = 0.22;

    private readonly WristTextureUpdateHost _textures;
    private readonly WristLayoutOptions _layoutOptions;
    private readonly WristInputAdapter _input;
    private readonly IWristOverlayDragDispatcher _drag;
    private readonly IWristPointerEventSource _pointerEvents;
    private readonly HashSet<uint> _primaryButtonsDown = [];
    private WristUiSnapshot? _publishedSnapshot;
    private WristTextureLayout? _publishedLayout;
    private (long Recorder, long Presentation)? _lastDispatchedRevision;
    private PendingMoveGesture? _moveGesture;
    private int _tickActive;

    public WristOverlayInteractionHost(
        WristTextureUpdateHost textures,
        WristLayoutOptions layoutOptions,
        WristInputAdapter input,
        IWristOverlayDragDispatcher drag,
        IWristPointerEventSource pointerEvents)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(layoutOptions);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(drag);
        ArgumentNullException.ThrowIfNull(pointerEvents);
        _textures = textures;
        _layoutOptions = layoutOptions;
        _input = input;
        _drag = drag;
        _pointerEvents = pointerEvents;
    }

    public async Task<WristOverlayInteractionTickResult> TickAsync(
        WristUiSnapshot snapshot,
        TimeSpan now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _tickActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A wrist overlay interaction tick is already active.");
        }
        try
        {
            var textureResult = _textures.Tick(snapshot, now);
            if (textureResult.Published)
            {
                _publishedLayout = WristTextureLayoutEngine.Layout(
                    snapshot,
                    _layoutOptions);
                _publishedSnapshot = snapshot;
            }

            var eventsPolled = 0;
            var actionDispatched = false;
            PendingDragRelease? pendingDragRelease = null;
            while (eventsPolled < MaxPointerEventsPerTick)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pointerEvent = _pointerEvents.PollPointerEvent();
                if (pointerEvent is null)
                {
                    break;
                }

                eventsPolled++;
                if (pointerEvent.Value.Kind == WristPointerEventKind.Move)
                {
                    if (_moveGesture is { } gesture &&
                        gesture.CursorIndex ==
                            pointerEvent.Value.CursorIndex)
                    {
                        gesture.Update(
                            pointerEvent.Value.PixelX,
                            pointerEvent.Value.PixelY);
                    }
                    continue;
                }
                if (pointerEvent.Value.Button != WristPointerButton.Primary)
                {
                    continue;
                }
                if (pointerEvent.Value.Kind == WristPointerEventKind.ButtonUp)
                {
                    _primaryButtonsDown.Remove(pointerEvent.Value.CursorIndex);
                    if (_moveGesture is { } gesture &&
                        gesture.CursorIndex ==
                            pointerEvent.Value.CursorIndex)
                    {
                        gesture.Update(
                            pointerEvent.Value.PixelX,
                            pointerEvent.Value.PixelY);
                        _moveGesture = null;
                        if (actionDispatched)
                        {
                            continue;
                        }
                        if (gesture.IsDragging)
                        {
                            pendingDragRelease = new PendingDragRelease(
                                gesture.ToDragDelta(),
                                gesture.Snapshot.Revision,
                                gesture.Snapshot.PresentationRevision);
                            continue;
                        }
                        if (_lastDispatchedRevision ==
                            (
                                gesture.Snapshot.Revision,
                                gesture.Snapshot.PresentationRevision))
                        {
                            continue;
                        }
                        var tapHandled = await _input.ActivateAtAsync(
                                gesture.Snapshot,
                                gesture.Layout,
                                gesture.StartX,
                                gesture.StartY,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (tapHandled)
                        {
                            actionDispatched = true;
                            _lastDispatchedRevision =
                                (
                                    gesture.Snapshot.Revision,
                                    gesture.Snapshot.PresentationRevision);
                        }
                    }
                    continue;
                }
                if (pointerEvent.Value.Kind !=
                        WristPointerEventKind.ButtonDown ||
                    !_primaryButtonsDown.Add(pointerEvent.Value.CursorIndex) ||
                    _publishedSnapshot is null ||
                    _publishedLayout is null)
                {
                    continue;
                }

                var target = _publishedLayout.HitTest(
                    pointerEvent.Value.PixelX,
                    pointerEvent.Value.PixelY);
                if (target?.Command == UiCommandId.OpenOverlayPositioning &&
                    !actionDispatched &&
                    _lastDispatchedRevision !=
                        (
                            _publishedSnapshot.Revision,
                            _publishedSnapshot.PresentationRevision))
                {
                    _moveGesture = new PendingMoveGesture(
                        _publishedSnapshot,
                        _publishedLayout,
                        pointerEvent.Value.CursorIndex,
                        pointerEvent.Value.PixelX,
                        pointerEvent.Value.PixelY);
                    continue;
                }
                if (target?.SemanticId == "recording.stop")
                {
                    pendingDragRelease = null;
                    _moveGesture = null;
                }
                if (actionDispatched ||
                    _lastDispatchedRevision ==
                        (
                            _publishedSnapshot.Revision,
                            _publishedSnapshot.PresentationRevision))
                {
                    continue;
                }
                var handled = await _input.ActivateAtAsync(
                        _publishedSnapshot,
                        _publishedLayout,
                        pointerEvent.Value.PixelX,
                        pointerEvent.Value.PixelY,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (handled)
                {
                    actionDispatched = true;
                    _lastDispatchedRevision =
                        (
                            _publishedSnapshot.Revision,
                            _publishedSnapshot.PresentationRevision);
                }
            }

            if (!actionDispatched && pendingDragRelease is { } release)
            {
                await _drag
                    .ReleaseDragAsync(release.Delta, cancellationToken)
                    .ConfigureAwait(false);
                actionDispatched = true;
                _lastDispatchedRevision =
                    (release.RecorderRevision, release.PresentationRevision);
            }

            return new WristOverlayInteractionTickResult(
                textureResult,
                eventsPolled,
                actionDispatched);
        }
        finally
        {
            Volatile.Write(ref _tickActive, 0);
        }
    }

    private sealed class PendingMoveGesture(
        WristUiSnapshot snapshot,
        WristTextureLayout layout,
        uint cursorIndex,
        int startX,
        int startY)
    {
        public WristUiSnapshot Snapshot { get; } = snapshot;

        public WristTextureLayout Layout { get; } = layout;

        public uint CursorIndex { get; } = cursorIndex;

        public int StartX { get; } = startX;

        public int StartY { get; } = startY;

        public int LastX { get; private set; } = startX;

        public int LastY { get; private set; } = startY;

        public bool IsDragging
        {
            get
            {
                var deltaX = LastX - StartX;
                var deltaY = LastY - StartY;
                return (long)deltaX * deltaX + (long)deltaY * deltaY >=
                    (long)DragActivationDistancePixels *
                    DragActivationDistancePixels;
            }
        }

        public void Update(int x, int y)
        {
            LastX = x;
            LastY = y;
        }

        public WristOverlayDragDelta ToDragDelta()
        {
            var metersPerPixel = OverlayWidthMeters / Layout.PixelWidth;
            return new WristOverlayDragDelta(
                (LastX - StartX) * metersPerPixel,
                (StartY - LastY) * metersPerPixel);
        }
    }

    private readonly record struct PendingDragRelease(
        WristOverlayDragDelta Delta,
        long RecorderRevision,
        long PresentationRevision);
}
