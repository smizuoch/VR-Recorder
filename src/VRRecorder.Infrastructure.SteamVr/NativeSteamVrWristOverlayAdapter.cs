using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class NativeSteamVrWristOverlayAdapter
    : IWristTexturePublisher, IWristPointerEventSource, IDisposable
{
    private readonly NativeSteamVrOverlayLifecycle _lifecycle;
    private int _disposed;

    public NativeSteamVrWristOverlayAdapter(
        string libraryPath,
        string installRoot,
        float widthInMeters =
            NativeSteamVrOverlayLifecycle.DefaultWidthInMeters)
        : this(new NativeSteamVrOverlayLifecycle(
            libraryPath,
            installRoot,
            widthInMeters))
    {
    }

    public NativeSteamVrWristOverlayAdapter(
        NativeSteamVrOverlayLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        _lifecycle = lifecycle;
    }

    public void Publish(WristTextureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfDisposed();
        _lifecycle.UpdateBgraTexture(
            frame.BgraPixels,
            frame.PixelWidth,
            frame.PixelHeight,
            frame.StrideBytes);
    }

    public void Show()
    {
        ThrowIfDisposed();
        _lifecycle.Show();
    }

    public WristPointerEvent? PollPointerEvent()
    {
        ThrowIfDisposed();
        var pointerEvent = _lifecycle.PollPointerEvent();
        return pointerEvent is null ? null : Map(pointerEvent.Value);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifecycle.Dispose();
        }
    }

    private static WristPointerEvent Map(
        SteamVrOverlayPointerEvent pointerEvent) => new(
        pointerEvent.Kind switch
        {
            SteamVrOverlayPointerEventKind.Move => WristPointerEventKind.Move,
            SteamVrOverlayPointerEventKind.ButtonDown =>
                WristPointerEventKind.ButtonDown,
            SteamVrOverlayPointerEventKind.ButtonUp =>
                WristPointerEventKind.ButtonUp,
            _ => throw new InvalidOperationException(
                "The native wrist pointer event kind is unsupported."),
        },
        pointerEvent.PixelX,
        pointerEvent.PixelY,
        pointerEvent.Button switch
        {
            SteamVrOverlayPointerButton.None => WristPointerButton.None,
            SteamVrOverlayPointerButton.Left => WristPointerButton.Primary,
            SteamVrOverlayPointerButton.Right => WristPointerButton.Secondary,
            SteamVrOverlayPointerButton.Middle => WristPointerButton.Middle,
            _ => throw new InvalidOperationException(
                "The native wrist pointer button is unsupported."),
        },
        pointerEvent.CursorIndex);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
}
