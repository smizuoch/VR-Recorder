namespace VRRecorder.Compliance.Staging;

internal sealed record WindowsRuntimeStagingManifest(
    int SchemaVersion,
    string ManifestSha256,
    string Profile,
    string RuntimeIdentifier,
    WindowsRuntimeLegalBundleAnchor LegalBundle,
    IReadOnlyList<WindowsRuntimeStagingEntry> Entries);

internal sealed record WindowsRuntimeLegalBundleAnchor(
    string BundleId,
    string ManifestSha256);

internal sealed record WindowsRuntimeStagingEntry(
    string Source,
    string Target,
    WindowsRuntimeRole Role,
    string ComponentId,
    string Platform,
    WindowsRuntimeDeploymentKind DeploymentKind,
    string Sha256,
    long Length);

public enum WindowsRuntimeRole
{
    FirstPartyNative,
    FfmpegRuntime,
    DiagnosticTool,
    OpenVrRuntime,
    OpenVrManifest,
    OpenVrBinding,
    SpoutRuntime,
    EncoderRuntime,
    ToolchainRuntime,
    FactorySelectionEvidence,
    ApplicationAsset,
}

public enum WindowsRuntimeDeploymentKind
{
    NativeLibrary,
    Executable,
    Asset,
    Evidence,
}
