using VRRecorder.Domain.Video;

namespace VRRecorder.Domain.Tests.Video;

public sealed class FrameRateTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    public void SupportsConfiguredRange(int value)
    {
        var frameRate = new FrameRate(value);

        Assert.Equal(value, frameRate.Value);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(121)]
    public void RejectsValuesOutsideConfiguredRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameRate(value));
    }
}
