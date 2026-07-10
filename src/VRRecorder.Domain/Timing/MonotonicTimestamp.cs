namespace VRRecorder.Domain.Timing;

public readonly record struct MonotonicTimestamp
{
    private MonotonicTimestamp(TimeSpan elapsed)
    {
        Elapsed = elapsed;
    }

    public TimeSpan Elapsed { get; }

    public static MonotonicTimestamp FromElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Monotonic elapsed time cannot be negative.");
        }

        return new MonotonicTimestamp(elapsed);
    }

    public MonotonicTimestamp Add(TimeSpan duration) =>
        FromElapsed(Elapsed + duration);
}
