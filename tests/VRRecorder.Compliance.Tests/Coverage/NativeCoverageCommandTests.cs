using System.IO.Compression;
using System.Text;
using VRRecorder.Compliance.Coverage;

namespace VRRecorder.Compliance.Tests.Coverage;

public sealed class NativeCoverageCommandTests
{
    [Fact]
    public void ReadsGzipArtifactsAndReturnsSuccessAtTheReleaseThreshold()
    {
        using var directory = TemporaryDirectory.Create();
        var artifact = WriteArtifact(directory.Path, coveredCount: 8);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = NativeCoverageCommand.Run(
            ["--source-fragment", "/src/VRRecorder.Native/src/", artifact],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("line=80.00%", output.ToString());
        Assert.Contains("branch=80.00%", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ReturnsFailureWithoutHidingAThresholdViolation()
    {
        using var directory = TemporaryDirectory.Create();
        var artifact = WriteArtifact(directory.Path, coveredCount: 7);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = NativeCoverageCommand.Run(
            ["--source-fragment", "/src/VRRecorder.Native/src/", artifact],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("line coverage 70.00%", error.ToString());
    }

    private static string WriteArtifact(string directory, int coveredCount)
    {
        var lines = Enumerable.Range(1, 10)
            .Select(index => $$"""
                {"line_number":{{index}},"count":{{(index <= coveredCount ? 1 : 0)}},"branches":[{"count":{{(index <= coveredCount ? 1 : 0)}}}]}
                """);
        var json = $$"""
            {"format_version":"1","files":[{"file":"/repo/src/VRRecorder.Native/src/a.cpp","lines":[{{string.Join(',', lines)}}]}]}
            """;
        var path = Path.Combine(directory, "a.gcov.json.gz");
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        gzip.Write(Encoding.UTF8.GetBytes(json));
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vrrecorder-native-coverage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
