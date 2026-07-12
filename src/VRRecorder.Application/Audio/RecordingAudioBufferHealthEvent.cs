using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public sealed record RecordingAudioBufferHealthEvent
{
    public RecordingAudioBufferHealthEvent(
        AudioInput input,
        AudioBufferHealthKind kind,
        long framePosition)
    {
        if (!Enum.IsDefined(input))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(framePosition);
        Input = input;
        Kind = kind;
        FramePosition = framePosition;
    }

    public AudioInput Input { get; }

    public AudioBufferHealthKind Kind { get; }

    public long FramePosition { get; }
}
