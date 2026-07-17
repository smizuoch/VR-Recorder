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
}
