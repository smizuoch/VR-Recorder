namespace VRRecorder.Domain.Timing;

public readonly record struct SelfTimer
{
    private SelfTimer(int seconds)
    {
        Seconds = seconds;
    }

    public int Seconds { get; }

    public bool IsEnabled => Seconds > 0;

    public static SelfTimer FromSeconds(int seconds) => seconds switch
    {
        0 or 3 or 5 or 10 => new SelfTimer(seconds),
        _ => throw new ArgumentOutOfRangeException(
            nameof(seconds),
            seconds,
            "Self timer must be Off, 3, 5, or 10 seconds."),
    };
}
