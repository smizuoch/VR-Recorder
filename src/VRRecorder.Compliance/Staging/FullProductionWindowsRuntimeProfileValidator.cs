namespace VRRecorder.Compliance.Staging;

internal static class FullProductionWindowsRuntimeProfileValidator
{
    private static readonly RequiredArtifact[] RequiredArtifacts =
    [
        Required(
            "vrrecorder_native.dll",
            WindowsRuntimeRole.FirstPartyNative,
            "vr-recorder",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "avcodec-62.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "avformat-62.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "avutil-60.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "swresample-6.dll",
            WindowsRuntimeRole.FfmpegRuntime,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "libvpl.dll",
            WindowsRuntimeRole.EncoderRuntime,
            "libvpl",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "msvcp140.dll",
            WindowsRuntimeRole.ToolchainRuntime,
            "msvc-runtime",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "msvcp140_atomic_wait.dll",
            WindowsRuntimeRole.ToolchainRuntime,
            "msvc-runtime",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "vcruntime140.dll",
            WindowsRuntimeRole.ToolchainRuntime,
            "msvc-runtime",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "vcruntime140_1.dll",
            WindowsRuntimeRole.ToolchainRuntime,
            "msvc-runtime",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "ffprobe.exe",
            WindowsRuntimeRole.DiagnosticTool,
            "ffmpeg",
            WindowsRuntimeDeploymentKind.Executable),
        Required(
            "openvr_api.dll",
            WindowsRuntimeRole.OpenVrRuntime,
            "openvr",
            WindowsRuntimeDeploymentKind.NativeLibrary),
        Required(
            "OpenVr/steamvr.vrmanifest",
            WindowsRuntimeRole.OpenVrManifest,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Required(
            "OpenVr/actions.json",
            WindowsRuntimeRole.OpenVrManifest,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Required(
            "OpenVr/bindings/knuckles.json",
            WindowsRuntimeRole.OpenVrBinding,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Required(
            "OpenVr/bindings/oculus_touch.json",
            WindowsRuntimeRole.OpenVrBinding,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Required(
            "OpenVr/bindings/vive_controller.json",
            WindowsRuntimeRole.OpenVrBinding,
            "openvr",
            WindowsRuntimeDeploymentKind.Asset),
        Required(
            "native-factory-selection.json",
            WindowsRuntimeRole.FactorySelectionEvidence,
            "vr-recorder",
            WindowsRuntimeDeploymentKind.Evidence),
    ];

    public static IReadOnlyList<ComplianceIssue> Validate(
        IEnumerable<WindowsRuntimeStagingEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var values = entries.ToArray();
        var issues = new List<ComplianceIssue>();
        foreach (var required in RequiredArtifacts)
        {
            var matches = values.Where(entry => string.Equals(
                    entry.Target,
                    required.Target,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "required-runtime-staging-artifact-missing",
                    required.Target));
                continue;
            }

            if (matches.Length != 1 ||
                matches[0].Role != required.Role ||
                !string.Equals(
                    matches[0].ComponentId,
                    required.ComponentId,
                    StringComparison.Ordinal) ||
                matches[0].DeploymentKind != required.DeploymentKind)
            {
                issues.Add(new ComplianceIssue(
                    "invalid-runtime-staging-artifact-identity",
                    required.Target));
            }
        }

        foreach (var entry in values.Where(entry =>
                     entry.Role != WindowsRuntimeRole.ApplicationAsset &&
                     !RequiredArtifacts.Any(required => string.Equals(
                         required.Target,
                         entry.Target,
                         StringComparison.Ordinal))))
        {
            issues.Add(new ComplianceIssue(
                "unexpected-runtime-staging-artifact",
                entry.Target));
        }

        return issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
    }

    private static RequiredArtifact Required(
        string target,
        WindowsRuntimeRole role,
        string componentId,
        WindowsRuntimeDeploymentKind deploymentKind) => new(
        target,
        role,
        componentId,
        deploymentKind);

    private sealed record RequiredArtifact(
        string Target,
        WindowsRuntimeRole Role,
        string ComponentId,
        WindowsRuntimeDeploymentKind DeploymentKind);
}
