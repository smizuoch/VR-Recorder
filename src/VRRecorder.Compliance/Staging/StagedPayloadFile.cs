namespace VRRecorder.Compliance.Staging;

public sealed record StagedPayloadFile(
    string RelativePath,
    string Sha256,
    long Length,
    StagedArtifactKind Kind);
