using VRRecorder.Domain.Storage;

namespace VRRecorder.Domain.Tests.Storage;

public sealed class OutputPathTests
{
    [Fact]
    public void StoresNormalizedAbsoluteDirectory()
    {
        var absolutePath = Path.Combine(
            Path.GetTempPath(),
            "vr-recorder-output",
            "..");

        var outputPath = new OutputPath(absolutePath);

        Assert.Equal(Path.GetFullPath(absolutePath), outputPath.FullPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-output")]
    public void RejectsMissingOrRelativeDirectory(string path)
    {
        Assert.Throws<ArgumentException>(() => new OutputPath(path));
    }
}
