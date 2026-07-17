using VRRecorder.Compliance;
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
        "--source-root <path> --output-parent <path>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        IWindowsRuntimeStagingRunner runner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(runner);

        if (!TryParse(args, out var stagingArguments))
        {
            await standardError.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        var result = await runner
            .ExecuteAsync(stagingArguments, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsStaged || result.ApprovedPropsPath is null)
        {
            foreach (var issue in result.Issues
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
            .WriteLineAsync(Path.GetFullPath(result.ApprovedPropsPath))
            .ConfigureAwait(false);
        return 0;
    }

    private static bool TryParse(
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
}
