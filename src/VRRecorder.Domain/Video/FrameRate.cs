namespace VRRecorder.Domain.Video;

public readonly record struct FrameRate
{
    public FrameRate(int value)
    {
        if (value is < 30 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Frame rate must be between 30 and 120 fps.");
        }

        Value = value;
    }

    public int Value { get; }
}
