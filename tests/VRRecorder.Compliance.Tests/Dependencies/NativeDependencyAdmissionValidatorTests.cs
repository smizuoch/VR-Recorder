using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Tests.Dependencies;

public sealed class NativeDependencyAdmissionValidatorTests
{
    [Fact]
    public void RetainsExplicitFirstPartyWindowsAndToolchainProvenance()
    {
        NativeLinkObservation[] observations =
        [
            new(
                "VRRecorder.App",
                "vrrecorder_native.dll",
                NativeLinkInputKind.DynamicLibrary,
                "windows-x64"),
            new(
                "vrrecorder_native",
                "kernel32.lib",
                NativeLinkInputKind.ImportLibrary,
                "windows-x64"),
            new(
                "vrrecorder_native",
                "Threads::Threads",
                NativeLinkInputKind.ToolchainTarget,
                "linux-x64"),
        ];
        NativeDependencyAdmission[] admissions =
        [
            new(
                "VRRecorder.App",
                "vrrecorder_native.dll",
                NativeLinkInputKind.DynamicLibrary,
                "windows-x64",
                NativeDependencyOrigin.FirstParty,
                ComponentId: null),
            new(
                "vrrecorder_native",
                "kernel32.lib",
                NativeLinkInputKind.ImportLibrary,
                "windows-x64",
                NativeDependencyOrigin.WindowsSystem,
                ComponentId: null),
            new(
                "vrrecorder_native",
                "Threads::Threads",
                NativeLinkInputKind.ToolchainTarget,
                "linux-x64",
                NativeDependencyOrigin.Toolchain,
                ComponentId: null),
        ];

        var report = NativeDependencyAdmissionValidator.Validate(
            observations,
            admissions,
            registeredComponentIds: []);

        Assert.Empty(report.Issues);
        Assert.Equal(3, report.Dependencies.Count);
        Assert.Collection(
            report.Dependencies,
            dependency => Assert.Equal(
                NativeDependencyOrigin.FirstParty,
                dependency.Origin),
            dependency => Assert.Equal(
                NativeDependencyOrigin.WindowsSystem,
                dependency.Origin),
            dependency => Assert.Equal(
                NativeDependencyOrigin.Toolchain,
                dependency.Origin));
        Assert.All(report.Dependencies, dependency =>
            Assert.Null(dependency.ComponentId));
    }

    [Fact]
    public void RejectsAnUnregisteredExternalImportLibrary()
    {
        NativeLinkObservation[] observations =
        [
            new(
                "vrrecorder_native",
                "Spout.lib",
                NativeLinkInputKind.ImportLibrary,
                "windows-x64"),
        ];

        var report = NativeDependencyAdmissionValidator.Validate(
            observations,
            admissions: [],
            registeredComponentIds: []);

        var issue = Assert.Single(report.Issues);
        Assert.Equal("unregistered-native-link", issue.Code);
        Assert.Equal("vrrecorder_native:Spout.lib", issue.Subject);
        Assert.Empty(report.Dependencies);
    }
}
