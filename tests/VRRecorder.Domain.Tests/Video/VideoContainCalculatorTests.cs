using VRRecorder.Domain.Video;

namespace VRRecorder.Domain.Tests.Video;

public sealed class VideoContainCalculatorTests
{
    [Fact]
    public void PortraitSourceIsCenteredInsideLandscapeCanvasWithoutCropping()
    {
        var source = new VideoGeometry(1080, 1920, VideoPixelFormat.Bgra8);
        var canvas = new VideoGeometry(1920, 1080, VideoPixelFormat.Nv12);

        var placement = VideoContainCalculator.Calculate(source, canvas);

        Assert.Equal(new VideoPlacement(657, 0, 606, 1080), placement);
    }
}
