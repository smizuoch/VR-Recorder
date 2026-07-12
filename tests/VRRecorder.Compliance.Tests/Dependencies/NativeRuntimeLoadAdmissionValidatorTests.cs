using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Tests.Dependencies;

public sealed class NativeRuntimeLoadAdmissionValidatorTests
{
    [Fact]
    public void DistinguishesReleaseArtifactsFromExactWindowsSystemLoads()
    {
        NativeRuntimeLoadObservation[] observations =
        [
            new(
                "VRRecorder.Infrastructure.Media",
                "vrrecorder_native.dll",
                NativeRuntimeLoadMechanism.NativeLibrary,
                "windows-x64"),
            new(
                "VRRecorder.Infrastructure.Osc",
                "dnsapi.dll",
                NativeRuntimeLoadMechanism.LibraryImport,
                "windows-x64"),
            new(
                "VRRecorder.Infrastructure.Storage",
                "shell32.dll",
                NativeRuntimeLoadMechanism.LibraryImport,
                "windows-x64"),
        ];
        NativeRuntimeLoadAdmission[] admissions =
        [
            new(
                "VRRecorder.Infrastructure.Media",
                "vrrecorder_native.dll",
                NativeRuntimeLoadMechanism.NativeLibrary,
                "windows-x64",
                NativeDependencyOrigin.FirstParty,
                NativeRuntimeIntegrity.ReleaseArtifact,
                ComponentId: null),
            new(
                "VRRecorder.Infrastructure.Osc",
                "dnsapi.dll",
                NativeRuntimeLoadMechanism.LibraryImport,
                "windows-x64",
                NativeDependencyOrigin.WindowsSystem,
                NativeRuntimeIntegrity.WindowsSystem,
                ComponentId: null),
            new(
                "VRRecorder.Infrastructure.Storage",
                "shell32.dll",
                NativeRuntimeLoadMechanism.LibraryImport,
                "windows-x64",
                NativeDependencyOrigin.WindowsSystem,
                NativeRuntimeIntegrity.WindowsSystem,
                ComponentId: null),
        ];

        var report = NativeRuntimeLoadAdmissionValidator.Validate(
            observations,
            admissions,
            registeredComponentIds: []);

        Assert.Empty(report.Issues);
        Assert.Equal(3, report.Dependencies.Count);
        Assert.Equal(
            NativeRuntimeIntegrity.ReleaseArtifact,
            report.Dependencies[0].Integrity);
        Assert.All(report.Dependencies.Skip(1), dependency =>
            Assert.Equal(
                NativeRuntimeIntegrity.WindowsSystem,
                dependency.Integrity));
    }

    [Fact]
    public void RejectsAnUnregisteredOpenVrRuntimeLoad()
    {
        NativeRuntimeLoadObservation[] observations =
        [
            new(
                "vrrecorder_native",
                "openvr_api.dll",
                NativeRuntimeLoadMechanism.LoadLibrary,
                "windows-x64"),
        ];

        var report = NativeRuntimeLoadAdmissionValidator.Validate(
            observations,
            admissions: [],
            registeredComponentIds: []);

        var issue = Assert.Single(report.Issues);
        Assert.Equal("unregistered-runtime-load", issue.Code);
        Assert.Equal("vrrecorder_native:openvr_api.dll", issue.Subject);
        Assert.Empty(report.Dependencies);
    }
}
