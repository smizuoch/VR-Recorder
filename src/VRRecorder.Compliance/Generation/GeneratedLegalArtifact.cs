namespace VRRecorder.Compliance.Generation;

public sealed record GeneratedLegalArtifact(
    string RelativePath,
    ReadOnlyMemory<byte> Content,
    string Sha256);
