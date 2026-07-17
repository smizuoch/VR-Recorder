using VRRecorder.Application.Encoding;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Encoding;

public sealed class EncoderProbeRequestTests
{
    [Theory]
    [InlineData(29.4, 1919, 1079, 30, 1920, 1080)]
    [InlineData(59.5, 1920, 1080, 60, 1920, 1080)]
    [InlineData(120.6, 1920, 1080, 120, 1920, 1080)]
    public void SignalProbePadsGeometryAndClampsRoundedFrameRate(
        double sourceFps,
        int sourceWidth,
        int sourceHeight,
        int expectedFps,
        int expectedWidth,
        int expectedHeight)
    {
        var signal = new StableVideoSignal(
            "sender",
            42,
            "GPU_1234",
            GpuVendor.Nvidia,
            sourceWidth,
            sourceHeight,
            VideoPixelFormat.Bgra8,
            sourceFps);

        var request = EncoderProbeRequest.ForSignal(
            EncoderKind.Nvenc,
            signal);

        Assert.Equal(EncoderKind.Nvenc, request.Encoder);
        Assert.Equal(42UL, request.AdapterLuid);
        Assert.Equal("GPU_1234", request.GpuIdentity);
        Assert.Equal(expectedWidth, request.Width);
        Assert.Equal(expectedHeight, request.Height);
        Assert.Equal(expectedFps, request.FrameRate.Value);
    }

    [Fact]
    public void ExplicitProbeRejectsUnknownIdentityAndInvalidGeometry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(encoder: (EncoderKind)int.MaxValue));
        Assert.Throws<ArgumentException>(() => Create(gpuIdentity: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(width: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(height: -1));
        Assert.Throws<ArgumentException>(() => Create(width: 1919));
        Assert.Throws<ArgumentException>(() => Create(height: 1079));
        Assert.Throws<ArgumentNullException>(() =>
            EncoderProbeRequest.ForSignal(EncoderKind.Nvenc, null!));
        Assert.Throws<ArgumentNullException>(() =>
            EncoderProbeRequest.ForSignal(
                EncoderKind.Nvenc,
                null!,
                1920,
                1080,
                new FrameRate(30)));

        var defaults = new EncoderProbeRequest(
            EncoderKind.MediaFoundationSoftware,
            0,
            "GPU");
        Assert.Equal(1920, defaults.Width);
        Assert.Equal(1080, defaults.Height);
        Assert.Equal(30, defaults.FrameRate.Value);
    }

    private static EncoderProbeRequest Create(
        EncoderKind encoder = EncoderKind.Nvenc,
        string gpuIdentity = "GPU_1234",
        int width = 1920,
        int height = 1080) =>
        new(
            encoder,
            42,
            gpuIdentity,
            width,
            height,
            new FrameRate(30));
}
