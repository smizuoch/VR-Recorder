using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Recording;

public sealed class StableVideoSignalTests
{
    [Fact]
    public void DiscoveredAndLegacySignalsExposeCanonicalSourceIdentity()
    {
        var discovered = Create();
        var legacy = new StableVideoSignal(640, 480);

        Assert.Equal("sender", discovered.SenderId);
        Assert.Equal(42UL, discovered.AdapterLuid);
        Assert.Equal("GPU_1234", discovered.GpuIdentity);
        Assert.Equal(GpuVendor.Nvidia, discovered.GpuVendor);
        Assert.Equal(VideoPixelFormat.Bgra8, discovered.PixelFormat);
        Assert.Equal(60, discovered.EstimatedSourceFramesPerSecond);
        Assert.True(discovered.HasDiscoveredSourceIdentity);
        Assert.False(legacy.HasDiscoveredSourceIdentity);
        Assert.Equal("legacy-source-640x480", legacy.SenderId);
    }

    [Fact]
    public void RejectsMalformedIdentityGeometryAdapterAndFrameRate()
    {
        Assert.Throws<ArgumentException>(() => Create(senderId: " "));
        Assert.Throws<ArgumentException>(() => Create(senderId: "sender\n"));
        Assert.Throws<ArgumentException>(() =>
            Create(senderId: new string('s', 4_097)));
        Assert.Throws<ArgumentException>(() => Create(gpuIdentity: "\ud800"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(adapterLuid: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(gpuVendor: (GpuVendor)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(width: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(height: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(pixelFormat: (VideoPixelFormat)int.MaxValue));

        foreach (var fps in new[]
                 {
                     double.NaN,
                     double.PositiveInfinity,
                     0,
                     -0.01,
                     1_000.01,
                 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Create(estimatedFramesPerSecond: fps));
        }
    }

    [Fact]
    public void ReplacesOnlyTheObservedVideoGeometry()
    {
        var signal = Create();

        var changed = signal.WithGeometry(
            new VideoGeometry(1_280, 720, VideoPixelFormat.Rgba8));

        Assert.Equal("sender", changed.SenderId);
        Assert.Equal(42UL, changed.AdapterLuid);
        Assert.Equal("GPU_1234", changed.GpuIdentity);
        Assert.Equal(GpuVendor.Nvidia, changed.GpuVendor);
        Assert.Equal(1_280, changed.Width);
        Assert.Equal(720, changed.Height);
        Assert.Equal(VideoPixelFormat.Rgba8, changed.PixelFormat);
        Assert.Equal(60, changed.EstimatedSourceFramesPerSecond);
        Assert.True(changed.HasDiscoveredSourceIdentity);
    }

    private static StableVideoSignal Create(
        string senderId = "sender",
        ulong adapterLuid = 42,
        string gpuIdentity = "GPU_1234",
        GpuVendor gpuVendor = GpuVendor.Nvidia,
        int width = 1920,
        int height = 1080,
        VideoPixelFormat pixelFormat = VideoPixelFormat.Bgra8,
        double estimatedFramesPerSecond = 60) =>
        new(
            senderId,
            adapterLuid,
            gpuIdentity,
            gpuVendor,
            width,
            height,
            pixelFormat,
            estimatedFramesPerSecond);
}
