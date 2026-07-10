using VRRecorder.Domain.Timing;

namespace VRRecorder.Domain.Tests.Timing;

public sealed class MonotonicTimestampTests
{
    [Fact]
    public void DurationIsAddedWithoutUsingWallClockTime()
    {
        var committedAt = MonotonicTimestamp.FromElapsed(
            TimeSpan.FromSeconds(100));

        var deadline = committedAt.Add(TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(103), deadline.Elapsed);
    }

    [Fact]
    public void NegativeElapsedTimeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MonotonicTimestamp.FromElapsed(TimeSpan.FromTicks(-1)));
    }
}
