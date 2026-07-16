namespace VRRecorder.Presentation.Wrist;

public enum WristPointerEventKind
{
    Move = 1,
    ButtonDown = 2,
    ButtonUp = 3,
}

public enum WristPointerButton
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Middle = 4,
}

public readonly record struct WristPointerEvent(
    WristPointerEventKind Kind,
    int PixelX,
    int PixelY,
    WristPointerButton Button,
    uint CursorIndex);

public interface IWristPointerEventSource
{
    WristPointerEvent? PollPointerEvent();
}
