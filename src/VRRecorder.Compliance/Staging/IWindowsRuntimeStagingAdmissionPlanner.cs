using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Staging;

internal interface IWindowsRuntimeStagingAdmissionPlanner
{
    Task<WindowsRuntimeStagingAdmissionResult> PlanAsync(
        WindowsRuntimeStagingManifest manifest,
        string sourceRoot,
        ApprovedReleaseGraph approvedGraph,
        string repositoryRoot,
        CancellationToken cancellationToken);
}
