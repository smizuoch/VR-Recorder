using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class FullProductionWindowsRuntimeProfileValidatorTests
{
    [Fact]
    public void ExactRequiredRuntimeClosureIsAccepted()
    {
        Assert.Empty(FullProductionWindowsRuntimeProfileValidator.Validate(
            RequiredEntries()));
    }

    [Theory]
    [InlineData("vrrecorder_native.dll")]
    [InlineData("avcodec-62.dll")]
    [InlineData("avformat-62.dll")]
    [InlineData("avutil-60.dll")]
    [InlineData("swresample-6.dll")]
    [InlineData("ffprobe.exe")]
    [InlineData("openvr_api.dll")]
    [InlineData("OpenVr/steamvr.vrmanifest")]
    [InlineData("OpenVr/actions.json")]
    [InlineData("OpenVr/bindings/knuckles.json")]
    [InlineData("OpenVr/bindings/oculus_touch.json")]
    [InlineData("OpenVr/bindings/vive_controller.json")]
    [InlineData("native-factory-selection.json")]
    public void EveryRequiredArtifactIsFailClosed(string target)
    {
        var entries = RequiredEntries()
            .Where(entry => !string.Equals(
                entry.Target,
                target,
                StringComparison.Ordinal))
            .ToArray();

        var issue = Assert.Single(
            FullProductionWindowsRuntimeProfileValidator.Validate(entries));

        Assert.Equal("required-runtime-staging-artifact-missing", issue.Code);
        Assert.Equal(target, issue.Subject);
    }

    [Fact]
    public void RuntimeMajorRoleAndOwnerAreExact()
    {
        var entries = RequiredEntries().ToList();
        var codec = entries.Single(entry =>
            entry.Target == "avcodec-62.dll");
        entries[entries.IndexOf(codec)] = codec with
        {
            ComponentId = "other",
        };

        var issue = Assert.Single(
            FullProductionWindowsRuntimeProfileValidator.Validate(entries));

        Assert.Equal("invalid-runtime-staging-artifact-identity", issue.Code);
        Assert.Equal("avcodec-62.dll", issue.Subject);
    }

    [Theory]
    [InlineData("avcodec-61.dll", WindowsRuntimeRole.FfmpegRuntime)]
    [InlineData("SpoutLibrary.dll", WindowsRuntimeRole.SpoutRuntime)]
    [InlineData("vpl.dll", WindowsRuntimeRole.EncoderRuntime)]
    public void UnapprovedRuntimeOrDriverPayloadIsRejected(
        string target,
        WindowsRuntimeRole role)
    {
        var entries = RequiredEntries().Append(Entry(
            target,
            role,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary));

        var issue = Assert.Single(
            FullProductionWindowsRuntimeProfileValidator.Validate(entries));

        Assert.Equal("unexpected-runtime-staging-artifact", issue.Code);
        Assert.Equal(target, issue.Subject);
    }

    [Fact]
    public void ApplicationPayloadAssetsRemainOpenForPostPublishSealing()
    {
        var entries = RequiredEntries().Append(Entry(
            "VRRecorder.Application.dll",
            WindowsRuntimeRole.ApplicationAsset,
            "vr-recorder",
            WindowsRuntimeDeploymentKind.Asset));

        Assert.Empty(FullProductionWindowsRuntimeProfileValidator.Validate(
            entries));
    }

    internal static WindowsRuntimeStagingEntry[] RequiredEntries() =>
    [
        Entry(
            "vrrecorder_native.dll",
            WindowsRuntimeRole.FirstPartyNative,
            "vr-recorder",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Entry(
            "avcodec-62.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Entry(
            "avformat-62.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Entry(
            "avutil-60.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Entry(
            "swresample-6.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Entry(
            "ffprobe.exe",
            WindowsRuntimeRole.DiagnosticTool,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.Executable),
        Entry(
            "openvr_api.dll",
            WindowsRuntimeRole.OpenVrRuntime,
            "openvr",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Entry(
            "OpenVr/steamvr.vrmanifest",
            WindowsRuntimeRole.OpenVrManifest,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Entry(
            "OpenVr/actions.json",
            WindowsRuntimeRole.OpenVrManifest,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Entry(
            "OpenVr/bindings/knuckles.json",
            WindowsRuntimeRole.OpenVrBinding,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Entry(
            "OpenVr/bindings/oculus_touch.json",
            WindowsRuntimeRole.OpenVrBinding,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Entry(
            "OpenVr/bindings/vive_controller.json",
            WindowsRuntimeRole.OpenVrBinding,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Entry(
            "native-factory-selection.json",
            WindowsRuntimeRole.FactorySelectionEvidence,
            "vr-recorder",
            WindowsRuntimeDeploymentKind.Evidence),
    ];

    private static WindowsRuntimeStagingEntry Entry(
        string target,
        WindowsRuntimeRole role,
        string componentId,
        WindowsRuntimeDeploymentKind deploymentKind) => new(
        target,
        target,
        role,
        componentId,
        "windows-x64",
        deploymentKind,
        new string('a', 64),
        17);
}
