namespace VRRecorder.Compliance.Coverage;

public sealed record NativeCoverageSummary(
    int TotalLines,
    int CoveredLines,
    int TotalBranches,
    int CoveredBranches,
    double LinePercentage,
    double BranchPercentage);
