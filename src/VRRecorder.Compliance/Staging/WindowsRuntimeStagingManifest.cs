namespace VRRecorder.Compliance.Staging;

internal sealed record WindowsRuntimeStagingManifest(
    int SchemaVersion,
    string ManifestSha256,
    IReadOnlyList<WindowsRuntimeStagingEntry> Entries);

internal sealed record WindowsRuntimeStagingEntry(
    string Source,
    string Target,
    WindowsRuntimeRole Role,
    string ComponentId,
    string Platform,
    WindowsRuntimeDeploymentKind DeploymentKind,
    string Sha256);

internal enum WindowsRuntimeRole
{
    FirstPartyNative,
    FfmpegRuntime,
    DiagnosticTool,
    OpenVrRuntime,
    OpenVrManifest,
    OpenVrBinding,
    SpoutRuntime,
    EncoderRuntime,
    FactorySelectionEvidence,
    ApplicationAsset,
}

internal enum WindowsRuntimeDeploymentKind
{
    NativeLibrary,
    Executable,
    Asset,
    Evidence,
}
