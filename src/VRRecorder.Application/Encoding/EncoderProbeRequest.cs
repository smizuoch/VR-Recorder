using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Encoding;

public sealed record EncoderProbeRequest
{
    public EncoderProbeRequest(
        EncoderKind encoder,
        ulong adapterLuid,
        string gpuIdentity)
        : this(
            encoder,
            adapterLuid,
            gpuIdentity,
            width: 1920,
            height: 1080,
            new FrameRate(30))
    {
    }

    public EncoderProbeRequest(
        EncoderKind encoder,
        ulong adapterLuid,
        string gpuIdentity,
        int width,
        int height,
        FrameRate frameRate)
    {
        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "The encoder kind is not defined.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gpuIdentity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((width & 1) != 0 || (height & 1) != 0)
        {
            throw new ArgumentException(
                "Encoder probe dimensions must be even for H.264 4:2:0 input.");
        }

        Encoder = encoder;
        AdapterLuid = adapterLuid;
        GpuIdentity = gpuIdentity;
        Width = width;
        Height = height;
        FrameRate = frameRate;
    }

    public EncoderKind Encoder { get; }

    public ulong AdapterLuid { get; }

    public string GpuIdentity { get; }

    public int Width { get; }

    public int Height { get; }

    public FrameRate FrameRate { get; }

    public static EncoderProbeRequest ForSignal(
        EncoderKind encoder,
        StableVideoSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var estimatedFrameRate = new FrameRate(Math.Clamp(
            (int)Math.Round(
                signal.EstimatedSourceFramesPerSecond,
                MidpointRounding.AwayFromZero),
            30,
            120));
        return ForSignal(
            encoder,
            signal,
            PadToEven(signal.Width),
            PadToEven(signal.Height),
            estimatedFrameRate);
    }

    public static EncoderProbeRequest ForSignal(
        EncoderKind encoder,
        StableVideoSignal signal,
        int outputWidth,
        int outputHeight,
        FrameRate outputFrameRate)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return new EncoderProbeRequest(
            encoder,
            signal.AdapterLuid,
            signal.GpuIdentity,
            outputWidth,
            outputHeight,
            outputFrameRate);
    }

    private static int PadToEven(int value) =>
        checked(value + (value & 1));
}
