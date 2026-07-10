namespace VRRecorder.Domain.Video;

public static class VideoContainCalculator
{
    public static VideoPlacement Calculate(
        VideoGeometry source,
        VideoGeometry canvas)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(canvas);

        var constrainedByWidth =
            (long)canvas.Width * source.Height <=
            (long)canvas.Height * source.Width;
        var width = constrainedByWidth
            ? FloorEven(canvas.Width, 1)
            : FloorEven((long)source.Width * canvas.Height, source.Height);
        var height = constrainedByWidth
            ? FloorEven((long)source.Height * canvas.Width, source.Width)
            : FloorEven(canvas.Height, 1);
        var offsetX = (canvas.Width - width) / 2;
        var offsetY = (canvas.Height - height) / 2;

        return new VideoPlacement(offsetX, offsetY, width, height);
    }

    private static int FloorEven(long numerator, int denominator)
    {
        var floored = checked((int)(numerator / denominator));
        return floored & ~1;
    }
}
