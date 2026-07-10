using VRRecorder.Domain.Video;

namespace VRRecorder.Domain.Tests.Video;

public sealed class VideoGeometryTests
{
    [Fact]
    public void HeightGreaterThanWidthIsPortrait()
    {
        var geometry = new VideoGeometry(
            width: 1080,
            height: 1920,
            VideoPixelFormat.Bgra8);

        Assert.Equal(VideoOrientation.Portrait, geometry.Orientation);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1080)]
    public void WidthAtLeastHeightIsLandscape(int width, int height)
    {
        var geometry = new VideoGeometry(width, height, VideoPixelFormat.Bgra8);

        Assert.Equal(VideoOrientation.Landscape, geometry.Orientation);
    }
}
