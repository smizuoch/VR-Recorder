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

public sealed class WristOverlayInteractionHost
    : IWristOverlayInteractionTicker
{
    public const int MaxPointerEventsPerTick = 64;

    private readonly WristTextureUpdateHost _textures;
    private readonly WristLayoutOptions _layoutOptions;
    private readonly WristInputAdapter _input;
    private readonly IWristPointerEventSource _pointerEvents;
    private readonly HashSet<uint> _primaryButtonsDown = [];
    private WristUiSnapshot? _publishedSnapshot;
    private WristTextureLayout? _publishedLayout;
    private long? _lastDispatchedRevision;
    private int _tickActive;

    public WristOverlayInteractionHost(
        WristTextureUpdateHost textures,
        WristLayoutOptions layoutOptions,
        WristInputAdapter input,
        IWristPointerEventSource pointerEvents)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(layoutOptions);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(pointerEvents);
        _textures = textures;
        _layoutOptions = layoutOptions;
        _input = input;
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
            while (eventsPolled < MaxPointerEventsPerTick)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pointerEvent = _pointerEvents.PollPointerEvent();
                if (pointerEvent is null)
                {
                    break;
                }

                eventsPolled++;
                if (pointerEvent.Value.Button != WristPointerButton.Primary)
                {
                    continue;
                }
                if (pointerEvent.Value.Kind == WristPointerEventKind.ButtonUp)
                {
                    _primaryButtonsDown.Remove(pointerEvent.Value.CursorIndex);
                    continue;
                }
                if (pointerEvent.Value.Kind !=
                        WristPointerEventKind.ButtonDown ||
                    !_primaryButtonsDown.Add(pointerEvent.Value.CursorIndex) ||
                    actionDispatched ||
                    _publishedSnapshot is null ||
                    _publishedLayout is null ||
                    _lastDispatchedRevision == _publishedSnapshot.Revision)
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
                    _lastDispatchedRevision = _publishedSnapshot.Revision;
                }
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
}
