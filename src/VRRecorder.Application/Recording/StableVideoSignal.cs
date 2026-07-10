using System.Text;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed record StableVideoSignal
{
    private const int MaximumIdentityUtf8Bytes = 4096;
    private const double MaximumEstimatedFramesPerSecond = 1_000;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public StableVideoSignal(
        string senderId,
        ulong adapterLuid,
        string gpuIdentity,
        GpuVendor gpuVendor,
        int width,
        int height,
        VideoPixelFormat pixelFormat,
        double estimatedSourceFramesPerSecond)
        : this(
            senderId,
            adapterLuid,
            gpuIdentity,
            gpuVendor,
            width,
            height,
            pixelFormat,
            estimatedSourceFramesPerSecond,
            hasDiscoveredSourceIdentity: true)
    {
    }

    public StableVideoSignal(int width, int height)
        : this(
            $"legacy-source-{width}x{height}",
            adapterLuid: 1,
            "legacy-unspecified-adapter",
            GpuVendor.Unknown,
            width,
            height,
            VideoPixelFormat.Bgra8,
            estimatedSourceFramesPerSecond: 30,
            hasDiscoveredSourceIdentity: false)
    {
    }

    private StableVideoSignal(
        string senderId,
        ulong adapterLuid,
        string gpuIdentity,
        GpuVendor gpuVendor,
        int width,
        int height,
        VideoPixelFormat pixelFormat,
        double estimatedSourceFramesPerSecond,
        bool hasDiscoveredSourceIdentity)
    {
        EnsureIdentity(senderId, nameof(senderId));
        EnsureIdentity(gpuIdentity, nameof(gpuIdentity));
        if (adapterLuid == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(adapterLuid),
                adapterLuid,
                "The video adapter LUID must be non-zero.");
        }

        if (!Enum.IsDefined(gpuVendor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(gpuVendor),
                gpuVendor,
                "The GPU vendor is not defined.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The source width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "The source height must be positive.");
        }

        if (!Enum.IsDefined(pixelFormat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelFormat),
                pixelFormat,
                "The source pixel format is not defined.");
        }

        if (!double.IsFinite(estimatedSourceFramesPerSecond) ||
            estimatedSourceFramesPerSecond <= 0 ||
            estimatedSourceFramesPerSecond > MaximumEstimatedFramesPerSecond)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedSourceFramesPerSecond),
                estimatedSourceFramesPerSecond,
                $"Estimated source FPS must be finite and within (0, {MaximumEstimatedFramesPerSecond}].");
        }

        SenderId = senderId;
        AdapterLuid = adapterLuid;
        GpuIdentity = gpuIdentity;
        GpuVendor = gpuVendor;
        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        EstimatedSourceFramesPerSecond = estimatedSourceFramesPerSecond;
        HasDiscoveredSourceIdentity = hasDiscoveredSourceIdentity;
    }

    public string SenderId { get; }

    public ulong AdapterLuid { get; }

    public string GpuIdentity { get; }

    public GpuVendor GpuVendor { get; }

    public int Width { get; }

    public int Height { get; }

    public VideoPixelFormat PixelFormat { get; }

    public double EstimatedSourceFramesPerSecond { get; }

    public bool HasDiscoveredSourceIdentity { get; }

    private static void EnsureIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Video-source identity text cannot contain control characters.",
                parameterName);
        }

        try
        {
            if (StrictUtf8.GetByteCount(value) > MaximumIdentityUtf8Bytes)
            {
                throw new ArgumentException(
                    $"Video-source identity text cannot exceed {MaximumIdentityUtf8Bytes} UTF-8 bytes.",
                    parameterName);
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Video-source identity text must be valid UTF-16 and UTF-8 encodable.",
                parameterName,
                exception);
        }
    }
}
