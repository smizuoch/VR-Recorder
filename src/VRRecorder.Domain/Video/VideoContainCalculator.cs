namespace VRRecorder.Domain.Video;

public static class VideoContainCalculator
{
    public static VideoPlacement Calculate(
        VideoGeometry source,
        VideoGeometry canvas)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(canvas);

        var scale = Math.Min(
            (double)canvas.Width / source.Width,
            (double)canvas.Height / source.Height);
        var width = FloorEven(source.Width * scale);
        var height = FloorEven(source.Height * scale);
        var offsetX = (canvas.Width - width) / 2;
        var offsetY = (canvas.Height - height) / 2;

        return new VideoPlacement(offsetX, offsetY, width, height);
    }

    private static int FloorEven(double value) =>
        (int)Math.Floor(value / 2) * 2;
}
