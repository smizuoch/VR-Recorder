using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Tests.Storage;

public sealed class PendingRecordingTests
{
    [Fact]
    public void NormalizesAbsolutePathsInOneDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pending", "..");

        var pending = new PendingRecording(
            Path.Combine(directory, "take.recording.mp4"),
            Path.Combine(directory, "take.mp4"));

        Assert.Equal(
            Path.GetFullPath(Path.Combine(directory, "take.recording.mp4")),
            pending.TemporaryPath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(directory, "take.mp4")),
            pending.FinalPath);
    }

    [Fact]
    public void RejectsRelativePaths()
    {
        Assert.Throws<ArgumentException>(() => new PendingRecording(
            "take.recording.mp4",
            "take.mp4"));
    }

    [Fact]
    public void RejectsPathsFromDifferentDirectories()
    {
        Assert.Throws<ArgumentException>(() => new PendingRecording(
            Path.Combine(Path.GetTempPath(), "one", "take.recording.mp4"),
            Path.Combine(Path.GetTempPath(), "two", "take.mp4")));
    }

    [Theory]
    [InlineData("take.tmp", "take.mp4")]
    [InlineData("take.recording.mp4", "take.recording.mp4")]
    [InlineData("take.recording.mp4", "take.mkv")]
    public void RejectsInvalidTemporaryOrFinalSuffixes(
        string temporaryName,
        string finalName)
    {
        Assert.Throws<ArgumentException>(() => new PendingRecording(
            Path.Combine(Path.GetTempPath(), temporaryName),
            Path.Combine(Path.GetTempPath(), finalName)));
    }
}
