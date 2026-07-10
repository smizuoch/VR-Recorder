using VRRecorder.Domain.Timing;

namespace VRRecorder.Domain.Tests.Timing;

public sealed class RecordingDurationTests
{
    [Fact]
    public void InfiniteHasNoFiniteSeconds()
    {
        var duration = RecordingDuration.Infinite;

        Assert.True(duration.IsInfinite);
        Assert.Null(duration.Seconds);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void SupportedAutoStopSecondsAreAccepted(int seconds)
    {
        var duration = RecordingDuration.FromSeconds(seconds);

        Assert.False(duration.IsInfinite);
        Assert.Equal(seconds, duration.Seconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(61)]
    public void UnsupportedAutoStopSecondsAreRejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingDuration.FromSeconds(seconds));
    }
}
