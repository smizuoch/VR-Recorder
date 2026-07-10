namespace VRRecorder.Domain.Video;

public sealed record VideoGeometry
{
    public VideoGeometry(int width, int height, VideoPixelFormat pixelFormat)
    {
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
}
