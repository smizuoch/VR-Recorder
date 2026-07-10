namespace VRRecorder.Application.Presentation;

[Flags]
public enum RecorderAvailableActions
{
    None = 0,
    Start = 1 << 0,
    Stop = 1 << 1,
    Cancel = 1 << 2,
    Retry = 1 << 3,
}
