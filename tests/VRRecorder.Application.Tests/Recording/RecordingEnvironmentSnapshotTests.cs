using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingEnvironmentSnapshotTests
{
    [Fact]
    public void AcceptsCanonicalReleaseEnvironmentIdentifiers()
    {
        var snapshot = new RecordingEnvironmentSnapshot(
            "1.2.3.4",
            "10.0.26100",
            RecordingProcessArchitecture.Arm64,
            "NVIDIA&DEV_2684",
            GpuVendor.Nvidia,
            "32.0.15.7688");

        Assert.Equal("1.2.3.4", snapshot.AppVersion);
        Assert.Equal("10.0.26100", snapshot.OsBuild);
        Assert.Equal(RecordingProcessArchitecture.Arm64, snapshot.Architecture);
        Assert.Equal("NVIDIA&DEV_2684", snapshot.GpuModel);
        Assert.Equal(GpuVendor.Nvidia, snapshot.GpuVendor);
        Assert.Equal("32.0.15.7688", snapshot.DriverVersion);
    }

    [Theory]
    [InlineData("1.2", "10.0.26100", "32.0.15.7688")]
    [InlineData("1.2.3.4.5", "10.0.26100", "32.0.15.7688")]
    [InlineData("1.02.3", "10.0.26100", "32.0.15.7688")]
    [InlineData("1..3", "10.0.26100", "32.0.15.7688")]
    [InlineData("1.a.3", "10.0.26100", "32.0.15.7688")]
    [InlineData("1.2.3", "10.0", "32.0.15.7688")]
    [InlineData("1.2.3", "10.0.26100", "32.0.15")]
    [InlineData("1.2.3", "10.0.26100", "32.0.15.07688")]
    public void RejectsNonCanonicalNumericVersions(
        string appVersion,
        string osBuild,
        string driverVersion)
    {
        Assert.Throws<ArgumentException>(() =>
            new RecordingEnvironmentSnapshot(
                appVersion,
                osBuild,
                RecordingProcessArchitecture.X64,
                "GPU_1234",
                GpuVendor.Unknown,
                driverVersion));
    }

    [Fact]
    public void RejectsUnknownEnumsAndNonCanonicalGpuModels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(architecture: (RecordingProcessArchitecture)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(vendor: (GpuVendor)int.MaxValue));
        Assert.Throws<ArgumentException>(() => Create(gpuModel: " "));
        Assert.Throws<ArgumentException>(() =>
            Create(gpuModel: new string('G', 65)));
        Assert.Throws<ArgumentException>(() =>
            Create(gpuModel: "NVIDIA DEV-2684"));
    }

    private static RecordingEnvironmentSnapshot Create(
        RecordingProcessArchitecture architecture =
            RecordingProcessArchitecture.X64,
        string gpuModel = "GPU_1234",
        GpuVendor vendor = GpuVendor.Unknown) =>
        new(
            "1.2.3",
            "10.0.26100",
            architecture,
            gpuModel,
            vendor,
            "32.0.15.7688");
}
