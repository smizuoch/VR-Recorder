namespace VRRecorder.Compliance.Staging;

internal interface IWindowsRuntimeStagingPublisher
{
    Task<WindowsRuntimeStagingPublication> PublishAsync(
        AdmittedWindowsRuntimeStagingPlan plan,
        string outputParent,
        CancellationToken cancellationToken);
}
