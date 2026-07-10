using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Packaging;

public sealed record ReleasePackageRequest(
    string StagingDirectory,
    string PackagePath,
    IReadOnlyList<RegisteredStagedArtifact> RegisteredArtifacts);
