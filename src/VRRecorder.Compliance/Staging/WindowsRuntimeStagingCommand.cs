using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Staging;

public sealed record WindowsRuntimeStagingCommandResult(
    string? ApprovedPropsPath,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsStaged =>
        !string.IsNullOrWhiteSpace(ApprovedPropsPath) && Issues.Count == 0;
}

public static class WindowsRuntimeStagingCommand
{
    public static async Task<WindowsRuntimeStagingCommandResult> ExecuteAsync(
        string repositoryRoot,
        string manifestPath,
        string sourceRoot,
        string outputParent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputParent);
        cancellationToken.ThrowIfCancellationRequested();

        var releaseGraph = RepositoryApprovedReleaseGraphBuilder.Build(
            repositoryRoot);
        if (!releaseGraph.IsApproved || releaseGraph.ApprovedGraph is null)
        {
            return new WindowsRuntimeStagingCommandResult(
                null,
                releaseGraph.Issues);
        }

        var result = await new WindowsRuntimeStagingOrchestrator()
            .StageAsync(
                new WindowsRuntimeStagingRequest(
                    manifestPath,
                    sourceRoot,
                    outputParent,
                    repositoryRoot,
                    releaseGraph.ApprovedGraph),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsStaged && result.Publication is not null
            ? new WindowsRuntimeStagingCommandResult(
                result.Publication.ApprovedPropsPath,
                [])
            : new WindowsRuntimeStagingCommandResult(null, result.Issues);
    }
}
