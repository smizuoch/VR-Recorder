using VRRecorder.Domain.Timing;

namespace VRRecorder.Domain.Tests.Timing;

public sealed class SelfTimerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void SupportedSecondsAreAccepted(int seconds)
    {
        var timer = SelfTimer.FromSeconds(seconds);

        Assert.Equal(seconds, timer.Seconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(11)]
    public void UnsupportedSecondsAreRejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SelfTimer.FromSeconds(seconds));
    }
}
