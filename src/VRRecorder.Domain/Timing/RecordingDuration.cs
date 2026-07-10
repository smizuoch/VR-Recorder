namespace VRRecorder.Domain.Timing;

public readonly record struct RecordingDuration
{
    private RecordingDuration(int? seconds)
    {
        Seconds = seconds;
    }

    public static RecordingDuration Infinite => new(null);

    public int? Seconds { get; }

    public bool IsInfinite => Seconds is null;

    public static RecordingDuration FromSeconds(int seconds) => seconds switch
    {
        3 or 5 or 10 or 30 or 60 => new RecordingDuration(seconds),
        _ => throw new ArgumentOutOfRangeException(
            nameof(seconds),
            seconds,
            "Recording duration must be 3, 5, 10, 30, or 60 seconds."),
    };
}
