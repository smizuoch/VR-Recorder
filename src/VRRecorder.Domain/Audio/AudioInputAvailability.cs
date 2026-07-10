namespace VRRecorder.Domain.Audio;

[Flags]
public enum AudioInputAvailability
{
    None = 0,
    Desktop = 1 << 0,
    Microphone = 1 << 1,
    All = Desktop | Microphone,
}
