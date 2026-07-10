namespace VRRecorder.Compliance.Staging;

public sealed record RegisteredStagedArtifact(
    string ComponentId,
    string RelativePath,
    string Sha256,
    StagedArtifactKind Kind);
