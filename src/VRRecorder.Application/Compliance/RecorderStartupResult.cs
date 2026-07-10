using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Compliance;

public sealed record RecorderStartupResult(
    RecorderState State,
    IReadOnlyList<LegalBundleIssue> Issues);
