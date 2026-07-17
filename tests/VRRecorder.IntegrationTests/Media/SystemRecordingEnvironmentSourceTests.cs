using System.Runtime.InteropServices;
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

    [Fact]
    public void CapturesIdentityWithoutPciPrefixAndWithCaseInsensitiveDriver()
    {
        var source = new SystemRecordingEnvironmentSource(
            "0.3.0",
            "10.0.26100",
            RecordingProcessArchitecture.Arm64);
        var signal = new StableVideoSignal(
            "sender",
            42,
            "VEN_1002&DEV_744C|DRIVER-24.7.1.0",
            GpuVendor.Amd,
            1920,
            1080,
            VideoPixelFormat.Bgra8,
            60);

        var snapshot = source.Capture(signal);

        Assert.Equal("ven_1002&dev_744c", snapshot.GpuModel);
        Assert.Equal("24.7.1.0", snapshot.DriverVersion);
        Assert.Equal(RecordingProcessArchitecture.Arm64, snapshot.Architecture);
    }

    [Theory]
    [InlineData("pci\\ven_10de")]
    [InlineData("pci\\bad_10de&dev_2684|driver-1.2.3.4")]
    [InlineData("pci\\ven_10d&dev_2684|driver-1.2.3.4")]
    [InlineData("pci\\ven_zzzz&dev_2684|driver-1.2.3.4")]
    [InlineData("pci\\ven_10de&bad_2684|driver-1.2.3.4")]
    [InlineData("pci\\ven_10de&dev_268|driver-1.2.3.4")]
    [InlineData("pci\\ven_10de&dev_zzzz|driver-1.2.3.4")]
    [InlineData("pci\\ven_10de&dev_2684")]
    public void RejectsNoncanonicalGpuIdentity(string gpuIdentity)
    {
        var source = new SystemRecordingEnvironmentSource(
            "0.3.0",
            "10.0.26100",
            RecordingProcessArchitecture.X64);
        var signal = new StableVideoSignal(
            "sender",
            42,
            gpuIdentity,
            GpuVendor.Nvidia,
            1920,
            1080,
            VideoPixelFormat.Bgra8,
            60);

        Assert.Throws<InvalidDataException>(() => source.Capture(signal));
    }

    [Fact]
    public void ProcessEnvironmentHelpersCoverSupportedArchitecturesAndVersions()
    {
        Assert.Equal(
            RecordingProcessArchitecture.X64,
            SystemRecordingEnvironmentSource.ConvertArchitecture(
                Architecture.X64));
        Assert.Equal(
            RecordingProcessArchitecture.Arm64,
            SystemRecordingEnvironmentSource.ConvertArchitecture(
                Architecture.Arm64));
        Assert.Throws<PlatformNotSupportedException>(() =>
            SystemRecordingEnvironmentSource.ConvertArchitecture(
                Architecture.X86));
        Assert.Equal(
            "1.2.3",
            SystemRecordingEnvironmentSource.FormatVersion(
                new Version(1, 2, 3)));
        Assert.Equal(
            "1.2.3.4",
            SystemRecordingEnvironmentSource.FormatVersion(
                new Version(1, 2, 3, 4)));
        Assert.Equal(
            "1.2.0",
            SystemRecordingEnvironmentSource.FormatVersion(
                new Version(1, 2)));
    }

    [Fact]
    public void CurrentProcessSourceProducesCanonicalSnapshot()
    {
        var source = SystemRecordingEnvironmentSource.ForCurrentProcess();
        var signal = new StableVideoSignal(
            "sender",
            42,
            "pci\\ven_10de&dev_2684|driver-1.2.3.4",
            GpuVendor.Nvidia,
            1920,
            1080,
            VideoPixelFormat.Bgra8,
            60);

        var snapshot = source.Capture(signal);

        Assert.Equal("ven_10de&dev_2684", snapshot.GpuModel);
        Assert.True(snapshot.AppVersion.Split('.').Length >= 3);
        Assert.True(snapshot.OsBuild.Split('.').Length >= 3);
    }
}
