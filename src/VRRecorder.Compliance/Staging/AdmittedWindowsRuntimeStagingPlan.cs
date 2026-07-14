using System.Collections.ObjectModel;

namespace VRRecorder.Compliance.Staging;

public sealed class AdmittedWindowsRuntimeStagingPlan
{
    internal AdmittedWindowsRuntimeStagingPlan(
        string manifestSha256,
        string sourceRoot,
        IReadOnlyList<AdmittedWindowsRuntimeStagingFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentNullException.ThrowIfNull(files);

        ManifestSha256 = manifestSha256;
        SourceRoot = sourceRoot;
        Files = new ReadOnlyCollection<AdmittedWindowsRuntimeStagingFile>(
            files.ToArray());
    }

    public string ManifestSha256 { get; }

    public string SourceRoot { get; }

    public IReadOnlyList<AdmittedWindowsRuntimeStagingFile> Files { get; }
}

public sealed class AdmittedWindowsRuntimeStagingFile
{
    internal AdmittedWindowsRuntimeStagingFile(
        string source,
        string target,
        WindowsRuntimeRole role,
        string componentId,
        WindowsRuntimeDeploymentKind deploymentKind,
        string sha256,
        long length,
        StagedArtifactKind kind)
    {
        Source = source;
        Target = target;
        Role = role;
        ComponentId = componentId;
        DeploymentKind = deploymentKind;
        Sha256 = sha256;
        Length = length;
        Kind = kind;
    }

    public string Source { get; }

    public string Target { get; }

    public WindowsRuntimeRole Role { get; }

    public string ComponentId { get; }

    public WindowsRuntimeDeploymentKind DeploymentKind { get; }

    public string Sha256 { get; }

    public long Length { get; }

    public StagedArtifactKind Kind { get; }
}
