using VRRecorder.Compliance.Coverage;

namespace VRRecorder.Compliance.Tests.Coverage;

public sealed class NativeGcovCoverageMetricsTests
{
    [Fact]
    public void MergesFirstPartyLineAndBranchCoverageAcrossTestExecutables()
    {
        var first = Document(
            "../../src/VRRecorder.Native/src/a.cpp",
            """
            {"line_number":10,"count":0,"branches":[{"count":0},{"count":2}]},
            {"line_number":11,"count":1,"branches":[]}
            """);
        var second = Document(
            "../../src/VRRecorder.Native/src/a.cpp",
            """
            {"line_number":10,"count":3,"branches":[{"count":1},{"count":0}]}
            """);
        var system = Document(
            "/usr/include/c++/13/vector",
            """
            {"line_number":99,"count":0,"branches":[{"count":0}]}
            """);

        var summary = NativeGcovCoverageMetrics.Evaluate(
            [first, second, system],
            "/src/VRRecorder.Native/src/");

        Assert.Equal(2, summary.TotalLines);
        Assert.Equal(2, summary.CoveredLines);
        Assert.Equal(2, summary.TotalBranches);
        Assert.Equal(2, summary.CoveredBranches);
        Assert.Equal(100, summary.LinePercentage);
        Assert.Equal(100, summary.BranchPercentage);
    }

    [Fact]
    public void ReportsCoverageBelowEightyPercentWithoutAReleaseThreshold()
    {
        var lines = Enumerable.Range(1, 10)
            .Select(index => $$"""
                {"line_number":{{index}},"count":{{(index <= 7 ? 1 : 0)}},"branches":[{"count":{{(index <= 7 ? 1 : 0)}}}]}
                """);
        var report = Document(
            "/repo/src/VRRecorder.Native/src/report.cpp",
            string.Join(',', lines));

        var summary = NativeGcovCoverageMetrics.Evaluate(
            [report],
            "/src/VRRecorder.Native/src/");

        Assert.Equal(70, summary.LinePercentage);
        Assert.Equal(70, summary.BranchPercentage);
    }

    [Fact]
    public void ExcludesCompilerGeneratedThrowEdgesFromSourceBranchCoverage()
    {
        var document = Document(
            "/repo/src/VRRecorder.Native/src/exception.cpp",
            """
            {"line_number":10,"count":1,"branches":[
              {"count":1,"throw":false},
              {"count":0,"throw":true}
            ]}
            """);

        var summary = NativeGcovCoverageMetrics.Evaluate(
            [document],
            "/src/VRRecorder.Native/src/");

        Assert.Equal(1, summary.TotalBranches);
        Assert.Equal(1, summary.CoveredBranches);
        Assert.Equal(100, summary.BranchPercentage);
    }

    private static string Document(string file, string lines) => $$"""
        {
          "format_version":"1",
          "files":[
            {"file":{{System.Text.Json.JsonSerializer.Serialize(file)}},"lines":[{{lines}}]}
          ]
        }
        """;
}
