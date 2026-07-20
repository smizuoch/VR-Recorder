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

internal sealed record WindowsStorePackagingValidationArguments(
    string PayloadRoot,
    string PayloadIdentityPath,
    string HardwareValidationReportPath,
    string HardwareValidationArtifactRoot,
    string CandidateOutputPath,
    string StoreName,
    string StorePublisher,
    string StorePublisherDisplayName);

internal sealed record WindowsStorePackagingValidationCommandResult(
    string? PayloadIdentityPath,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsValidated =>
        PayloadIdentityPath is not null && Issues.Count == 0;
}

internal interface IWindowsStorePackagingValidationRunner
{
    Task<WindowsStorePackagingValidationCommandResult> ExecuteAsync(
        WindowsStorePackagingValidationArguments arguments,
        CancellationToken cancellationToken);
}

internal sealed class WindowsStorePackagingValidationRunner
    : IWindowsStorePackagingValidationRunner
{
    public async Task<WindowsStorePackagingValidationCommandResult>
        ExecuteAsync(
            WindowsStorePackagingValidationArguments arguments,
            CancellationToken cancellationToken)
    {
        byte[] identityContent;
        byte[] reportContent;
        try
        {
            identityContent = await File.ReadAllBytesAsync(
                    arguments.PayloadIdentityPath,
                    cancellationToken)
                .ConfigureAwait(false);
            reportContent = await File.ReadAllBytesAsync(
                    arguments.HardwareValidationReportPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return new WindowsStorePackagingValidationCommandResult(
                null,
                [
                    new ComplianceIssue(
                        "store-packaging-input-read-failed",
                        exception is IOException
                            ? exception.Message
                            : "input-path"),
                ]);
        }

        var validation = await WindowsStorePackagingInputValidator
            .ValidateAsync(
                arguments.PayloadRoot,
                identityContent,
                reportContent,
                arguments.HardwareValidationArtifactRoot,
                arguments.CandidateOutputPath,
                new MicrosoftStoreIdentity(
                    arguments.StoreName,
                    arguments.StorePublisher,
                    arguments.StorePublisherDisplayName),
                cancellationToken)
            .ConfigureAwait(false);
        return new WindowsStorePackagingValidationCommandResult(
            validation.IsValidated
                ? arguments.PayloadIdentityPath
                : null,
            validation.Issues);
    }
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
        "--identity-output <path>" +
        " OR VRRecorder.ReleaseTool validate-store-packaging-input " +
        "--payload-root <path> --payload-identity <path> " +
        "--hardware-report <path> --hardware-artifacts-root <path> " +
        "--candidate-output <path> --store-name <name> " +
        "--store-publisher <publisher> " +
        "--store-publisher-display-name <name>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        IWindowsRuntimeStagingRunner stagingRunner,
        IWindowsPayloadSealingRunner sealingRunner,
        CancellationToken cancellationToken) =>
        await RunAsync(
                args,
                standardOutput,
                standardError,
                stagingRunner,
                sealingRunner,
                new WindowsStorePackagingValidationRunner(),
                cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        IWindowsRuntimeStagingRunner stagingRunner,
        IWindowsPayloadSealingRunner sealingRunner,
        IWindowsStorePackagingValidationRunner storeValidationRunner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(stagingRunner);
        ArgumentNullException.ThrowIfNull(sealingRunner);
        ArgumentNullException.ThrowIfNull(storeValidationRunner);

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

        if (TryParseStoreValidation(
                args,
                out var storeValidationArguments))
        {
            var validationResult = await storeValidationRunner
                .ExecuteAsync(storeValidationArguments, cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(
                    validationResult.PayloadIdentityPath,
                    validationResult.Issues,
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

    private static bool TryParseStoreValidation(
        IReadOnlyList<string> args,
        out WindowsStorePackagingValidationArguments parsed)
    {
        parsed = null!;
        if (args.Count != 17 ||
            args[0] != "validate-store-packaging-input")
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (option is not (
                    "--payload-root" or
                    "--payload-identity" or
                    "--hardware-report" or
                    "--hardware-artifacts-root" or
                    "--candidate-output" or
                    "--store-name" or
                    "--store-publisher" or
                    "--store-publisher-display-name") ||
                string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(option, value))
            {
                return false;
            }
        }

        if (values.Count != 8)
        {
            return false;
        }

        parsed = new WindowsStorePackagingValidationArguments(
            values["--payload-root"],
            values["--payload-identity"],
            values["--hardware-report"],
            values["--hardware-artifacts-root"],
            values["--candidate-output"],
            values["--store-name"],
            values["--store-publisher"],
            values["--store-publisher-display-name"]);
        return true;
    }
}
