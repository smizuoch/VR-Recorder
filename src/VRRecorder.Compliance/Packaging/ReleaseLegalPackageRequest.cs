using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Packaging;

public sealed record ReleaseLegalPackageRequest(
    NormalizedComponentGraph ComponentGraph,
    SpdxGenerationContext GenerationContext,
    string StagingDirectory,
    string PackagePath,
    IReadOnlyList<RegisteredStagedArtifact> ApprovedPayloadArtifacts);
