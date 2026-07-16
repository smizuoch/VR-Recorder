namespace VRRecorder.Infrastructure.SteamVr;

public enum SteamVrOverlayPointerEventKind : uint
{
    Move = 1,
    ButtonDown = 2,
    ButtonUp = 3,
}

public enum SteamVrOverlayPointerButton : uint
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
}

public readonly record struct SteamVrOverlayPointerEvent(
    SteamVrOverlayPointerEventKind Kind,
    int PixelX,
    int PixelY,
    SteamVrOverlayPointerButton Button,
    uint CursorIndex);
