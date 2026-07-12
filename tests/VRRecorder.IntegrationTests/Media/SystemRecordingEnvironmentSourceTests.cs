using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class SystemRecordingEnvironmentSourceTests
{
    [Fact]
    public void CapturesCanonicalEnvironmentWithoutPrivateGpuIdentity()
    {
        var source = new SystemRecordingEnvironmentSource(
            "0.3.0",
            "10.0.26100",
            RecordingProcessArchitecture.X64);
        var signal = new StableVideoSignal(
            "private-sender",
            42,
            "pci\\ven_10de&dev_2684|driver-32.0.15.6094|private-machine",
            GpuVendor.Nvidia,
            1920,
            1080,
            VideoPixelFormat.Bgra8,
            60);

        var snapshot = source.Capture(signal);

        Assert.Equal("0.3.0", snapshot.AppVersion);
        Assert.Equal("10.0.26100", snapshot.OsBuild);
        Assert.Equal(RecordingProcessArchitecture.X64, snapshot.Architecture);
        Assert.Equal("ven_10de&dev_2684", snapshot.GpuModel);
        Assert.Equal(GpuVendor.Nvidia, snapshot.GpuVendor);
        Assert.Equal("32.0.15.6094", snapshot.DriverVersion);
        Assert.DoesNotContain("private", snapshot.GpuModel);
        Assert.DoesNotContain("private", snapshot.DriverVersion);
    }
}
