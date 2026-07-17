using VRRecorder.Compliance.Coverage;

namespace VRRecorder.Compliance.Tests.Coverage;

public sealed class NativeGcovCoverageGateTests
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

        var summary = NativeGcovCoverageGate.Evaluate(
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
    public void EnforcesEightyPercentForBothLinesAndBranches()
    {
        var eightyPercent = Enumerable.Range(1, 10)
            .Select(index => $$"""
                {"line_number":{{index}},"count":{{(index <= 8 ? 1 : 0)}},"branches":[{"count":{{(index <= 8 ? 1 : 0)}}}]}
                """);
        var passing = Document(
            "/repo/src/VRRecorder.Native/src/pass.cpp",
            string.Join(',', eightyPercent));

        var summary = NativeGcovCoverageGate.EnsureReleaseThreshold(
            [passing],
            "/src/VRRecorder.Native/src/");
        Assert.Equal(80, summary.LinePercentage);
        Assert.Equal(80, summary.BranchPercentage);

        var failing = passing.Replace(
            "{\"line_number\":8,\"count\":1",
            "{\"line_number\":8,\"count\":0",
            StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidDataException>(() =>
            NativeGcovCoverageGate.EnsureReleaseThreshold(
                [failing],
                "/src/VRRecorder.Native/src/"));
        Assert.Contains("line coverage", exception.Message);
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

        var summary = NativeGcovCoverageGate.Evaluate(
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
