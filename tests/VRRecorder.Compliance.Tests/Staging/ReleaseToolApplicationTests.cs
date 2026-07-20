using VRRecorder.Compliance.Staging;
using VRRecorder.Compliance.Distribution;
using VRRecorder.ReleaseTool;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class ReleaseToolApplicationTests
{
    [Fact]
    public async Task ExactStageCommandPrintsOnlyTheApprovedPropsPath()
    {
        WindowsRuntimeStagingArguments? observed = null;
        var runner = new CallbackRunner(arguments =>
        {
            observed = arguments;
            return new WindowsRuntimeStagingCommandResult(
                Path.Combine("out", "windows-runtime-digest",
                    "ApprovedWindowsRuntime.props"),
                []);
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseToolApplication.RunAsync(
            Arguments(),
            output,
            error,
            runner,
            RejectingSealingRunner.Instance,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(observed);
        Assert.Equal("repository", observed.RepositoryRoot);
        Assert.Equal("runtime-manifest.json", observed.ManifestPath);
        Assert.Equal("runtime-input", observed.SourceRoot);
        Assert.Equal("staged", observed.OutputParent);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                "out",
                "windows-runtime-digest",
                "ApprovedWindowsRuntime.props")) +
            Environment.NewLine,
            output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RejectedAdmissionIsSortedAndReturnsOne()
    {
        var runner = new CallbackRunner(_ =>
            new WindowsRuntimeStagingCommandResult(
                null,
                [
                    new ComplianceIssue("z-last", "b"),
                    new ComplianceIssue("a-first", "c"),
                ]));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseToolApplication.RunAsync(
            Arguments(),
            output,
            error,
            runner,
            RejectingSealingRunner.Instance,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            $"a-first: c{Environment.NewLine}" +
            $"z-last: b{Environment.NewLine}",
            error.ToString());
    }

    [Theory]
    [InlineData()]
    [InlineData("unknown")]
    [InlineData("stage-windows-runtime", "--repository-root", "repo")]
    [InlineData(
        "stage-windows-runtime",
        "--repository-root", "repo",
        "--repository-root", "other",
        "--source-root", "source",
        "--output-parent", "out")]
    public async Task InvalidOrDuplicateArgumentsReturnUsage(
        params string[] args)
    {
        var runner = new CallbackRunner(_ =>
            throw new InvalidOperationException("runner must not execute"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseToolApplication.RunAsync(
            args,
            output,
            error,
            runner,
            RejectingSealingRunner.Instance,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.StartsWith("Usage:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactSealCommandPrintsOnlyTheIdentityPath()
    {
        WindowsPayloadSealingArguments? observed = null;
        var runner = new CallbackSealingRunner(arguments =>
        {
            observed = arguments;
            return new WindowsPayloadSealingCommandResult(
                Path.Combine("out", "payload-identity.json"),
                []);
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseToolApplication.RunAsync(
            [
                "seal-windows-payload",
                "--publish-root", "publish",
                "--approved-props", "ApprovedWindowsRuntime.props",
                "--identity-output", "payload-identity.json",
            ],
            output,
            error,
            new CallbackRunner(_ => throw new InvalidOperationException()),
            runner,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(observed);
        Assert.Equal("publish", observed.PublishRoot);
        Assert.Equal("ApprovedWindowsRuntime.props", observed.ApprovedPropsPath);
        Assert.Equal("payload-identity.json", observed.IdentityOutputPath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("out", "payload-identity.json")) +
            Environment.NewLine,
            output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExactStoreValidationCommandKeepsAllIdentityInputs()
    {
        WindowsStorePackagingValidationArguments? observed = null;
        var runner = new CallbackStoreValidationRunner(arguments =>
        {
            observed = arguments;
            return new WindowsStorePackagingValidationCommandResult(
                Path.Combine("evidence", "payload-identity.json"),
                []);
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseToolApplication.RunAsync(
            [
                "validate-store-packaging-input",
                "--payload-root", "payload",
                "--payload-identity", "payload-identity.json",
                "--hardware-report", "hardware-report.json",
                "--hardware-artifacts-root", "hardware-artifacts",
                "--candidate-output", "VRRecorder.msix",
                "--store-name", "VRRecorder.Project",
                "--store-publisher", "CN=publisher",
                "--store-publisher-display-name", "VR Recorder Project",
            ],
            output,
            error,
            new CallbackRunner(_ => throw new InvalidOperationException()),
            RejectingSealingRunner.Instance,
            runner,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(observed);
        Assert.Equal("payload", observed.PayloadRoot);
        Assert.Equal("payload-identity.json", observed.PayloadIdentityPath);
        Assert.Equal("hardware-report.json",
            observed.HardwareValidationReportPath);
        Assert.Equal("hardware-artifacts",
            observed.HardwareValidationArtifactRoot);
        Assert.Equal("VRRecorder.msix", observed.CandidateOutputPath);
        Assert.Equal("VRRecorder.Project", observed.StoreName);
        Assert.Equal("CN=publisher", observed.StorePublisher);
        Assert.Equal("VR Recorder Project",
            observed.StorePublisherDisplayName);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                "evidence",
                "payload-identity.json")) + Environment.NewLine,
            output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExactLegalBundleCommandKeepsReleaseIdentityInputs()
    {
        LegalBundleGenerationArguments? observed = null;
        var runner = new CallbackLegalBundleRunner(arguments =>
        {
            observed = arguments;
            return new LegalBundleGenerationCommandResult(
                Path.Combine("artifacts", "legal"),
                []);
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseToolApplication.RunAsync(
            [
                "generate-legal-bundle",
                "--repository-root", "repository",
                "--output-directory", "artifacts/legal",
                "--product-name", "VR-Recorder",
                "--product-version", "0.1.0",
                "--document-namespace",
                "https://example.invalid/spdx/vr-recorder/0.1.0/revision",
                "--created-at-utc", "2026-07-20T00:00:00Z",
                "--creator", "Organization: VR-Recorder Project",
            ],
            output,
            error,
            new CallbackRunner(_ => throw new InvalidOperationException()),
            RejectingSealingRunner.Instance,
            new CallbackStoreValidationRunner(_ =>
                throw new InvalidOperationException()),
            runner,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(observed);
        Assert.Equal("repository", observed.RepositoryRoot);
        Assert.Equal("artifacts/legal", observed.OutputDirectory);
        Assert.Equal("VR-Recorder", observed.ProductName);
        Assert.Equal("0.1.0", observed.ProductVersion);
        Assert.Equal("2026-07-20T00:00:00Z", observed.CreatedAtUtc);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("artifacts", "legal")) +
            Environment.NewLine,
            output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExactStoreSubmissionPreflightCommandKeepsEvidencePaths()
    {
        WindowsStoreSubmissionPreflightArguments? observed = null;
        var runner = new CallbackSubmissionPreflightRunner(arguments =>
        {
            observed = arguments;
            return new WindowsStoreSubmissionPreflightCommandResult(
                "VRRecorder.msix",
                []);
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseToolApplication.RunAsync(
            [
                "validate-store-submission-preflight",
                "--package", "VRRecorder.msix",
                "--packaging-identity", "packaging.json",
                "--sideload-evidence", "sideload.json",
                "--wack-evidence", "wack.xml",
                "--final-scan-evidence", "final-scan.json",
                "--packaged-hardware-report", "packaged-hardware.json",
                "--packaged-hardware-artifacts-root", "packaged-artifacts",
            ],
            output,
            error,
            new CallbackRunner(_ => throw new InvalidOperationException()),
            RejectingSealingRunner.Instance,
            new CallbackStoreValidationRunner(_ =>
                throw new InvalidOperationException()),
            new CallbackLegalBundleRunner(_ =>
                throw new InvalidOperationException()),
            runner,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(observed);
        Assert.Equal("VRRecorder.msix", observed.PackagePath);
        Assert.Equal("packaging.json", observed.PackagingIdentityPath);
        Assert.Equal("sideload.json", observed.SideloadEvidencePath);
        Assert.Equal("wack.xml", observed.WackEvidencePath);
        Assert.Equal("final-scan.json", observed.FinalScanEvidencePath);
        Assert.Equal("packaged-hardware.json",
            observed.PackagedHardwareReportPath);
        Assert.Equal("packaged-artifacts",
            observed.PackagedHardwareArtifactRoot);
        Assert.Equal(
            Path.GetFullPath("VRRecorder.msix") + Environment.NewLine,
            output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    private static string[] Arguments() =>
    [
        "stage-windows-runtime",
        "--repository-root", "repository",
        "--manifest", "runtime-manifest.json",
        "--source-root", "runtime-input",
        "--output-parent", "staged",
    ];

    private sealed class CallbackRunner(
        Func<WindowsRuntimeStagingArguments,
            WindowsRuntimeStagingCommandResult> callback)
        : IWindowsRuntimeStagingRunner
    {
        public Task<WindowsRuntimeStagingCommandResult> ExecuteAsync(
            WindowsRuntimeStagingArguments arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(arguments));
    }

    private sealed class CallbackSealingRunner(
        Func<WindowsPayloadSealingArguments,
            WindowsPayloadSealingCommandResult> callback)
        : IWindowsPayloadSealingRunner
    {
        public Task<WindowsPayloadSealingCommandResult> ExecuteAsync(
            WindowsPayloadSealingArguments arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(arguments));
    }

    private sealed class RejectingSealingRunner : IWindowsPayloadSealingRunner
    {
        public static RejectingSealingRunner Instance { get; } = new();

        public Task<WindowsPayloadSealingCommandResult> ExecuteAsync(
            WindowsPayloadSealingArguments arguments,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
            "sealing runner must not execute");
    }

    private sealed class CallbackStoreValidationRunner(
        Func<WindowsStorePackagingValidationArguments,
            WindowsStorePackagingValidationCommandResult> callback)
        : IWindowsStorePackagingValidationRunner
    {
        public Task<WindowsStorePackagingValidationCommandResult>
            ExecuteAsync(
                WindowsStorePackagingValidationArguments arguments,
                CancellationToken cancellationToken) =>
            Task.FromResult(callback(arguments));
    }

    private sealed class CallbackLegalBundleRunner(
        Func<LegalBundleGenerationArguments,
            LegalBundleGenerationCommandResult> callback)
        : ILegalBundleGenerationRunner
    {
        public Task<LegalBundleGenerationCommandResult> ExecuteAsync(
            LegalBundleGenerationArguments arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(arguments));
    }

    private sealed class CallbackSubmissionPreflightRunner(
        Func<WindowsStoreSubmissionPreflightArguments,
            WindowsStoreSubmissionPreflightCommandResult> callback)
        : IWindowsStoreSubmissionPreflightRunner
    {
        public Task<WindowsStoreSubmissionPreflightCommandResult> ExecuteAsync(
            WindowsStoreSubmissionPreflightArguments arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(arguments));
    }
}
