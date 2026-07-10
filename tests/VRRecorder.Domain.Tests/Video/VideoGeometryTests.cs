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
}
