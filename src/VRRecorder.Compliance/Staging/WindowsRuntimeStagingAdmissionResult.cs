namespace VRRecorder.Compliance.Staging;

internal sealed record WindowsRuntimeStagingAdmissionResult(
    AdmittedWindowsRuntimeStagingPlan? Plan,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsAdmitted => Plan is not null && Issues.Count == 0;
}
