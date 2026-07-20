using VRRecorder.Compliance;
using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Repository;
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

internal sealed record LegalBundleGenerationArguments(
    string RepositoryRoot,
    string OutputDirectory,
    string ProductName,
    string ProductVersion,
    string DocumentNamespace,
    string CreatedAtUtc,
    string Creator);

internal sealed record LegalBundleGenerationCommandResult(
    string? BundleDirectory,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsGenerated =>
        BundleDirectory is not null && Issues.Count == 0;
}

internal interface ILegalBundleGenerationRunner
{
    Task<LegalBundleGenerationCommandResult> ExecuteAsync(
        LegalBundleGenerationArguments arguments,
        CancellationToken cancellationToken);
}

internal sealed record WindowsStoreSubmissionPreflightArguments(
    string PackagePath,
    string PackagingIdentityPath,
    string SideloadEvidencePath,
    string WackEvidencePath,
    string FinalScanEvidencePath,
    string PackagedHardwareReportPath,
    string PackagedHardwareArtifactRoot);

internal sealed record WindowsStoreSubmissionPreflightCommandResult(
    string? PackagePath,
    IReadOnlyList<ComplianceIssue> Issues);

internal sealed record WindowsStorePublicReleaseArguments(
    string PackagePath,
    string PackagingIdentityPath,
    string SideloadEvidencePath,
    string WackEvidencePath,
    string FinalScanEvidencePath,
    string PackagedHardwareReportPath,
    string PackagedHardwareArtifactRoot,
    string PartnerCenterEvidencePath,
    string CertificationReportPath,
    string FlightReportPath);

internal interface IWindowsStoreSubmissionPreflightRunner
{
    Task<WindowsStoreSubmissionPreflightCommandResult> ExecuteAsync(
        WindowsStoreSubmissionPreflightArguments arguments,
        CancellationToken cancellationToken);
}

internal sealed class WindowsStoreSubmissionPreflightRunner
    : IWindowsStoreSubmissionPreflightRunner
{
    public async Task<WindowsStoreSubmissionPreflightCommandResult>
        ExecuteAsync(
            WindowsStoreSubmissionPreflightArguments arguments,
            CancellationToken cancellationToken)
    {
        try
        {
            var packagingIdentity = await File.ReadAllBytesAsync(
                    arguments.PackagingIdentityPath,
                    cancellationToken)
                .ConfigureAwait(false);
            var sideloadEvidence = await File.ReadAllBytesAsync(
                    arguments.SideloadEvidencePath,
                    cancellationToken)
                .ConfigureAwait(false);
            var wackEvidence = await File.ReadAllBytesAsync(
                    arguments.WackEvidencePath,
                    cancellationToken)
                .ConfigureAwait(false);
            var finalScanEvidence = await File.ReadAllBytesAsync(
                    arguments.FinalScanEvidencePath,
                    cancellationToken)
                .ConfigureAwait(false);
            var packagedHardwareReport = await File.ReadAllBytesAsync(
                    arguments.PackagedHardwareReportPath,
                    cancellationToken)
                .ConfigureAwait(false);
            var result = WindowsStoreSubmissionPreflightValidator.Validate(
                arguments.PackagePath,
                packagingIdentity,
                sideloadEvidence,
                wackEvidence,
                finalScanEvidence,
                packagedHardwareReport,
                arguments.PackagedHardwareArtifactRoot);
            return new WindowsStoreSubmissionPreflightCommandResult(
                result.IsSubmissionReady ? arguments.PackagePath : null,
                result.Issues);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return new WindowsStoreSubmissionPreflightCommandResult(
                null,
                [new ComplianceIssue(
                    "store-submission-preflight-read-failed",
                    exception is IOException
                        ? exception.Message
                        : "input-path")]);
        }
    }
}

internal static class WindowsStorePublicReleaseRunner
{
    public static async Task<WindowsStoreSubmissionPreflightCommandResult>
        ExecuteAsync(
            WindowsStorePublicReleaseArguments arguments,
            CancellationToken cancellationToken)
    {
        try
        {
            var packagingIdentity = await ReadAsync(
                arguments.PackagingIdentityPath,
                cancellationToken);
            var sideloadEvidence = await ReadAsync(
                arguments.SideloadEvidencePath,
                cancellationToken);
            var wackEvidence = await ReadAsync(
                arguments.WackEvidencePath,
                cancellationToken);
            var finalScanEvidence = await ReadAsync(
                arguments.FinalScanEvidencePath,
                cancellationToken);
            var packagedHardwareReport = await ReadAsync(
                arguments.PackagedHardwareReportPath,
                cancellationToken);
            var partnerCenterEvidence = await ReadAsync(
                arguments.PartnerCenterEvidencePath,
                cancellationToken);
            var certificationReport = await ReadAsync(
                arguments.CertificationReportPath,
                cancellationToken);
            var flightReport = await ReadAsync(
                arguments.FlightReportPath,
                cancellationToken);
            var validation = WindowsStorePublicReleaseValidator.Validate(
                arguments.PackagePath,
                packagingIdentity,
                sideloadEvidence,
                wackEvidence,
                finalScanEvidence,
                packagedHardwareReport,
                arguments.PackagedHardwareArtifactRoot,
                partnerCenterEvidence,
                certificationReport,
                flightReport);
            return new WindowsStoreSubmissionPreflightCommandResult(
                validation.IsPublishEligible ? arguments.PackagePath : null,
                validation.Issues);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return new WindowsStoreSubmissionPreflightCommandResult(
                null,
                [new ComplianceIssue(
                    "store-public-release-read-failed",
                    exception is IOException
                        ? exception.Message
                        : "input-path")]);
        }
    }

    private static Task<byte[]> ReadAsync(
        string path,
        CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(path, cancellationToken);
}

internal sealed class LegalBundleGenerationRunner
    : ILegalBundleGenerationRunner
{
    public async Task<LegalBundleGenerationCommandResult> ExecuteAsync(
        LegalBundleGenerationArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eligibility = RepositoryApprovedReleaseGraphBuilder.Build(
            arguments.RepositoryRoot);
        if (eligibility.ApprovedGraph is null || eligibility.Issues.Count != 0)
        {
            return new LegalBundleGenerationCommandResult(
                null,
                eligibility.Issues);
        }

        if (!DateTimeOffset.TryParseExact(
                arguments.CreatedAtUtc,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var createdAtUtc))
        {
            return Reject("legal-generation-time-invalid", arguments.CreatedAtUtc);
        }

        try
        {
            var artifactSet = LegalArtifactSetGenerator.Generate(
                new SpdxGenerationContext(
                    arguments.ProductName,
                    arguments.ProductVersion,
                    arguments.DocumentNamespace,
                    createdAtUtc,
                    arguments.Creator),
                eligibility.ApprovedGraph);
            await LegalArtifactDirectoryWriter.WriteAsync(
                    arguments.OutputDirectory,
                    artifactSet,
                    cancellationToken)
                .ConfigureAwait(false);
            var issues = await LegalArtifactDirectoryVerifier.VerifyAsync(
                    arguments.OutputDirectory,
                    artifactSet,
                    cancellationToken)
                .ConfigureAwait(false);
            return issues.Count == 0
                ? new LegalBundleGenerationCommandResult(
                    arguments.OutputDirectory,
                    [])
                : new LegalBundleGenerationCommandResult(null, issues);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or
            ArgumentException or NotSupportedException)
        {
            return Reject(
                "legal-bundle-generation-failed",
                exception is IOException
                    ? exception.Message
                    : "generation-input");
        }
    }

    private static LegalBundleGenerationCommandResult Reject(
        string code,
        string subject) => new(
        null,
        [new ComplianceIssue(code, subject)]);
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
        "--store-publisher-display-name <name>" +
        " OR VRRecorder.ReleaseTool generate-legal-bundle " +
        "--repository-root <path> --output-directory <path> " +
        "--product-name <name> --product-version <version> " +
        "--document-namespace <absolute-uri> " +
        "--created-at-utc <yyyy-MM-ddTHH:mm:ssZ> --creator <creator>" +
        " OR VRRecorder.ReleaseTool validate-store-submission-preflight " +
        "--package <path> --packaging-identity <path> " +
        "--sideload-evidence <path> --wack-evidence <path> " +
        "--final-scan-evidence <path> " +
        "--packaged-hardware-report <path> " +
        "--packaged-hardware-artifacts-root <path>" +
        " OR VRRecorder.ReleaseTool validate-store-public-release " +
        "--package <path> --packaging-identity <path> " +
        "--sideload-evidence <path> --wack-evidence <path> " +
        "--final-scan-evidence <path> " +
        "--packaged-hardware-report <path> " +
        "--packaged-hardware-artifacts-root <path> " +
        "--partner-center-evidence <path> " +
        "--certification-report <path> --flight-report <path>";

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
                new LegalBundleGenerationRunner(),
                new WindowsStoreSubmissionPreflightRunner(),
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
        => await RunAsync(
                args,
                standardOutput,
                standardError,
                stagingRunner,
                sealingRunner,
                storeValidationRunner,
                new LegalBundleGenerationRunner(),
                new WindowsStoreSubmissionPreflightRunner(),
                cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        IWindowsRuntimeStagingRunner stagingRunner,
        IWindowsPayloadSealingRunner sealingRunner,
        IWindowsStorePackagingValidationRunner storeValidationRunner,
        ILegalBundleGenerationRunner legalBundleRunner,
        CancellationToken cancellationToken)
        => await RunAsync(
                args,
                standardOutput,
                standardError,
                stagingRunner,
                sealingRunner,
                storeValidationRunner,
                legalBundleRunner,
                new WindowsStoreSubmissionPreflightRunner(),
                cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        IWindowsRuntimeStagingRunner stagingRunner,
        IWindowsPayloadSealingRunner sealingRunner,
        IWindowsStorePackagingValidationRunner storeValidationRunner,
        ILegalBundleGenerationRunner legalBundleRunner,
        IWindowsStoreSubmissionPreflightRunner submissionPreflightRunner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(stagingRunner);
        ArgumentNullException.ThrowIfNull(sealingRunner);
        ArgumentNullException.ThrowIfNull(storeValidationRunner);
        ArgumentNullException.ThrowIfNull(legalBundleRunner);
        ArgumentNullException.ThrowIfNull(submissionPreflightRunner);

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

        if (TryParseLegalBundleGeneration(
                args,
                out var legalBundleArguments))
        {
            var generationResult = await legalBundleRunner
                .ExecuteAsync(legalBundleArguments, cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(
                    generationResult.BundleDirectory,
                    generationResult.Issues,
                    standardOutput,
                    standardError)
                .ConfigureAwait(false);
        }

        if (TryParseStoreSubmissionPreflight(
                args,
                out var submissionPreflightArguments))
        {
            var preflightResult = await submissionPreflightRunner
                .ExecuteAsync(submissionPreflightArguments, cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(
                    preflightResult.PackagePath,
                    preflightResult.Issues,
                    standardOutput,
                    standardError)
                .ConfigureAwait(false);
        }

        if (TryParseStorePublicRelease(
                args,
                out var storePublicReleaseArguments))
        {
            var publicReleaseResult = await WindowsStorePublicReleaseRunner
                .ExecuteAsync(
                    storePublicReleaseArguments,
                    cancellationToken)
                .ConfigureAwait(false);
            return await CompleteAsync(
                    publicReleaseResult.PackagePath,
                    publicReleaseResult.Issues,
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

    private static bool TryParseLegalBundleGeneration(
        IReadOnlyList<string> args,
        out LegalBundleGenerationArguments parsed)
    {
        parsed = null!;
        if (args.Count != 15 || args[0] != "generate-legal-bundle")
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
                    "--output-directory" or
                    "--product-name" or
                    "--product-version" or
                    "--document-namespace" or
                    "--created-at-utc" or
                    "--creator") ||
                string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(option, value))
            {
                return false;
            }
        }

        if (values.Count != 7)
        {
            return false;
        }

        parsed = new LegalBundleGenerationArguments(
            values["--repository-root"],
            values["--output-directory"],
            values["--product-name"],
            values["--product-version"],
            values["--document-namespace"],
            values["--created-at-utc"],
            values["--creator"]);
        return true;
    }

    private static bool TryParseStoreSubmissionPreflight(
        IReadOnlyList<string> args,
        out WindowsStoreSubmissionPreflightArguments parsed)
    {
        parsed = null!;
        if (args.Count != 15 ||
            args[0] != "validate-store-submission-preflight")
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (option is not (
                    "--package" or
                    "--packaging-identity" or
                    "--sideload-evidence" or
                    "--wack-evidence" or
                    "--final-scan-evidence" or
                    "--packaged-hardware-report" or
                    "--packaged-hardware-artifacts-root") ||
                string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(option, value))
            {
                return false;
            }
        }

        if (values.Count != 7)
        {
            return false;
        }

        parsed = new WindowsStoreSubmissionPreflightArguments(
            values["--package"],
            values["--packaging-identity"],
            values["--sideload-evidence"],
            values["--wack-evidence"],
            values["--final-scan-evidence"],
            values["--packaged-hardware-report"],
            values["--packaged-hardware-artifacts-root"]);
        return true;
    }

    private static bool TryParseStorePublicRelease(
        IReadOnlyList<string> args,
        out WindowsStorePublicReleaseArguments parsed)
    {
        parsed = null!;
        if (args.Count != 21 ||
            args[0] != "validate-store-public-release")
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (option is not (
                    "--package" or
                    "--packaging-identity" or
                    "--sideload-evidence" or
                    "--wack-evidence" or
                    "--final-scan-evidence" or
                    "--packaged-hardware-report" or
                    "--packaged-hardware-artifacts-root" or
                    "--partner-center-evidence" or
                    "--certification-report" or
                    "--flight-report") ||
                string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(option, value))
            {
                return false;
            }
        }

        if (values.Count != 10)
        {
            return false;
        }

        parsed = new WindowsStorePublicReleaseArguments(
            values["--package"],
            values["--packaging-identity"],
            values["--sideload-evidence"],
            values["--wack-evidence"],
            values["--final-scan-evidence"],
            values["--packaged-hardware-report"],
            values["--packaged-hardware-artifacts-root"],
            values["--partner-center-evidence"],
            values["--certification-report"],
            values["--flight-report"]);
        return true;
    }
}
