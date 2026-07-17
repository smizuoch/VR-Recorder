using VRRecorder.Compliance.Coverage;

namespace VRRecorder.Compliance.Tests.Coverage;

public sealed class ManagedCoberturaCoverageCommandTests
{
    private static readonly string[] Assemblies =
    [
        "VRRecorder.Application",
        "VRRecorder.Compliance",
        "VRRecorder.Domain",
        "VRRecorder.Infrastructure.Media",
        "VRRecorder.Infrastructure.Osc",
        "VRRecorder.Infrastructure.SteamVr",
        "VRRecorder.Infrastructure.Storage",
        "VRRecorder.Presentation.Wrist",
    ];

    [Fact]
    public void AcceptsEveryRequiredAssemblyAtExactlyEightyPercent()
    {
        using var report = TemporaryReport.Create(Document());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ManagedCoberturaCoverageCommand.Run(
            [report.Path],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "VRRecorder.Compliance: line=80.00%, branch=80.00%",
            output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Theory]
    [InlineData("0.799", "0.81", "line")]
    [InlineData("0.81", "0.799", "branch")]
    public void RejectsAnyAssemblyBelowEitherThreshold(
        string lineRate,
        string branchRate,
        string metric)
    {
        using var report = TemporaryReport.Create(Document(
            "VRRecorder.Compliance",
            lineRate,
            branchRate));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ManagedCoberturaCoverageCommand.Run(
            [report.Path],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains(
            $"VRRecorder.Compliance {metric} coverage 79.90% is below 80.00%",
            error.ToString());
    }

    [Fact]
    public void RejectsAMissingRequiredAssembly()
    {
        using var report = TemporaryReport.Create(Document(
            omittedAssembly: "VRRecorder.Presentation.Wrist"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ManagedCoberturaCoverageCommand.Run(
            [report.Path],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "required managed coverage package is missing: VRRecorder.Presentation.Wrist",
            error.ToString());
    }

    [Fact]
    public void RejectsADuplicatePackage()
    {
        var document = Document().Replace(
            "<packages>",
            """
            <packages>
                <package name="VRRecorder.Application" line-rate="0.8" branch-rate="0.8" />
            """,
            StringComparison.Ordinal);
        using var report = TemporaryReport.Create(document);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ManagedCoberturaCoverageCommand.Run(
            [report.Path],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("invalid", error.ToString());
    }

    [Fact]
    public void RejectsAnInvalidReport()
    {
        using var report = TemporaryReport.Create("<coverage />");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ManagedCoberturaCoverageCommand.Run(
            [report.Path],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.NotEqual(string.Empty, error.ToString());
    }

    [Fact]
    public void RejectsMissingReportArgument()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ManagedCoberturaCoverageCommand.Run(
            [],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Usage", error.ToString());
    }

    private static string Document(
        string? changedAssembly = null,
        string lineRate = "0.8",
        string branchRate = "0.8",
        string? omittedAssembly = null)
    {
        var packages = Assemblies
            .Where(assembly => assembly != omittedAssembly)
            .Select(assembly =>
            {
                var packageLineRate = assembly == changedAssembly
                    ? lineRate
                    : "0.8";
                var packageBranchRate = assembly == changedAssembly
                    ? branchRate
                    : "0.8";
                return $"<package name=\"{assembly}\" line-rate=\"{packageLineRate}\" branch-rate=\"{packageBranchRate}\" />";
            });
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                {string.Join(Environment.NewLine, packages)}
              </packages>
            </coverage>
            """;
    }

    private sealed class TemporaryReport : IDisposable
    {
        private TemporaryReport(string path) => Path = path;

        public string Path { get; }

        public static TemporaryReport Create(string content)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vrrecorder-managed-coverage-{Guid.NewGuid():N}.xml");
            File.WriteAllText(path, content);
            return new TemporaryReport(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
