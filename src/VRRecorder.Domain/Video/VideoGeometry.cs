namespace VRRecorder.Domain.Video;

public sealed record VideoGeometry
{
    public VideoGeometry(int width, int height, VideoPixelFormat pixelFormat)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Video width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "Video height must be positive.");
        }

        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
    }

    public int Width { get; }

    public int Height { get; }

    public VideoPixelFormat PixelFormat { get; }

    public VideoOrientation Orientation => Height > Width
        ? VideoOrientation.Portrait
        : VideoOrientation.Landscape;

    public VideoGeometry PadForChroma420()
    {
        var paddedWidth = Width % 2 == 0 ? Width : Width + 1;
        var paddedHeight = Height % 2 == 0 ? Height : Height + 1;
        return new VideoGeometry(paddedWidth, paddedHeight, PixelFormat);
    }
}
