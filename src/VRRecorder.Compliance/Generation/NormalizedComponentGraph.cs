using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Generation;

public sealed record NormalizedComponentGraph(
    IReadOnlyList<NuGetPackage> Dependencies,
    IReadOnlyList<NormalizedComponent> Components);
