using VRRecorder.Compliance;
using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.ReleaseTool;

internal sealed record WindowsRuntimeStagingArguments(
    string RepositoryRoot,
    string ManifestPath,
    string SourceRoot,
    string OutputParent);

internal interface IWindowsRuntimeStagingRunner
{
    Task<WindowsRuntimeStagingCommandResult> ExecuteAsync(
        WindowsRuntimeStagingArguments arguments,
        CancellationToken cancellationToken);
}

internal sealed record WindowsPayloadSealingArguments(
    string PublishRoot,
    string ApprovedPropsPath,
    string IdentityOutputPath);

internal sealed record WindowsPayloadSealingCommandResult(
    string? IdentityPath,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsSealed => IdentityPath is not null && Issues.Count == 0;
}

internal interface IWindowsPayloadSealingRunner
{
    Task<WindowsPayloadSealingCommandResult> ExecuteAsync(
        WindowsPayloadSealingArguments arguments,
        CancellationToken cancellationToken);
}

internal sealed class WindowsPayloadSealingRunner
    : IWindowsPayloadSealingRunner
{
    public async Task<WindowsPayloadSealingCommandResult> ExecuteAsync(
        WindowsPayloadSealingArguments arguments,
        CancellationToken cancellationToken)
    {
        var seal = await new WindowsPostPublishPayloadSealer()
            .SealAsync(
                arguments.PublishRoot,
                arguments.ApprovedPropsPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (!seal.IsSealed || seal.Payload is null)
        {
            return new WindowsPayloadSealingCommandResult(null, seal.Issues);
        }

        var publication = await WindowsApplicationPayloadIdentityPublisher
            .PublishAsync(
                seal.Payload,
                arguments.IdentityOutputPath,
                cancellationToken)
            .ConfigureAwait(false);
        return new WindowsPayloadSealingCommandResult(
            publication.IdentityPath,
            publication.Issues);
    }
}

internal sealed class WindowsRuntimeStagingRunner
    : IWindowsRuntimeStagingRunner
{
    public Task<WindowsRuntimeStagingCommandResult> ExecuteAsync(
        WindowsRuntimeStagingArguments arguments,
        CancellationToken cancellationToken) =>
        WindowsRuntimeStagingCommand.ExecuteAsync(
            arguments.RepositoryRoot,
            arguments.ManifestPath,
            arguments.SourceRoot,
            arguments.OutputParent,
            cancellationToken);
}

internal static class ReleaseToolApplication
{
    private const string Usage =
        "Usage: VRRecorder.ReleaseTool stage-windows-runtime " +
        "--repository-root <path> --manifest <path> " +
        "--source-root <path> --output-parent <path>" +
        " OR VRRecorder.ReleaseTool seal-windows-payload " +
        "--publish-root <path> --approved-props <path> " +
        "--identity-output <path>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        IWindowsRuntimeStagingRunner stagingRunner,
        IWindowsPayloadSealingRunner sealingRunner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(stagingRunner);
        ArgumentNullException.ThrowIfNull(sealingRunner);

        if (TryParseStaging(args, out var stagingArguments))
        {
            var stagingResult = await stagingRunner
                .ExecuteAsync(stagingArguments, cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(
                    stagingResult.ApprovedPropsPath,
                    stagingResult.Issues,
                    standardOutput,
                    standardError)
                .ConfigureAwait(false);
        }

        if (TryParseSealing(args, out var sealingArguments))
        {
            var sealingResult = await sealingRunner
                .ExecuteAsync(sealingArguments, cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(
                    sealingResult.IdentityPath,
                    sealingResult.Issues,
                    standardOutput,
                    standardError)
                .ConfigureAwait(false);
        }

        await standardError.WriteLineAsync(Usage).ConfigureAwait(false);
        return 2;
    }

    private static async Task<int> CompleteAsync(
        string? successPath,
        IReadOnlyList<ComplianceIssue> issues,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (successPath is null || issues.Count != 0)
        {
            foreach (var issue in issues
                         .OrderBy(issue => issue.Code, StringComparer.Ordinal)
                         .ThenBy(issue => issue.Subject, StringComparer.Ordinal))
            {
                await standardError
                    .WriteLineAsync($"{issue.Code}: {issue.Subject}")
                    .ConfigureAwait(false);
            }

            return 1;
        }

        await standardOutput
            .WriteLineAsync(Path.GetFullPath(successPath))
            .ConfigureAwait(false);
        return 0;
    }

    private static bool TryParseStaging(
        IReadOnlyList<string> args,
        out WindowsRuntimeStagingArguments parsed)
    {
        parsed = null!;
        if (args.Count != 9 ||
            !string.Equals(
                args[0],
                "stage-windows-runtime",
                StringComparison.Ordinal))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (option is not (
                    "--repository-root" or
                    "--manifest" or
                    "--source-root" or
                    "--output-parent") ||
                string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(option, value))
            {
                return false;
            }
        }

        if (values.Count != 4)
        {
            return false;
        }

        parsed = new WindowsRuntimeStagingArguments(
            values["--repository-root"],
            values["--manifest"],
            values["--source-root"],
            values["--output-parent"]);
        return true;
    }

    private static bool TryParseSealing(
        IReadOnlyList<string> args,
        out WindowsPayloadSealingArguments parsed)
    {
        parsed = null!;
        if (args.Count != 7 ||
            args[0] != "seal-windows-payload")
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (option is not (
                    "--publish-root" or
                    "--approved-props" or
                    "--identity-output") ||
                string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(option, value))
            {
                return false;
            }
        }

        if (values.Count != 3)
        {
            return false;
        }

        parsed = new WindowsPayloadSealingArguments(
            values["--publish-root"],
            values["--approved-props"],
            values["--identity-output"]);
        return true;
    }
}
