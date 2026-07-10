namespace VRRecorder.Application.Storage;

public sealed record PendingRecording
{
    public PendingRecording(string TemporaryPath, string FinalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TemporaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(FinalPath);
        if (!Path.IsPathFullyQualified(TemporaryPath) ||
            !Path.IsPathFullyQualified(FinalPath))
        {
            throw new ArgumentException(
                "Pending recording paths must be absolute.");
        }

        var normalizedTemporaryPath = Path.GetFullPath(TemporaryPath);
        var normalizedFinalPath = Path.GetFullPath(FinalPath);
        if (!normalizedTemporaryPath.EndsWith(
                ".recording.mp4",
                StringComparison.OrdinalIgnoreCase) ||
            !normalizedFinalPath.EndsWith(
                ".mp4",
                StringComparison.OrdinalIgnoreCase) ||
            normalizedFinalPath.EndsWith(
                ".recording.mp4",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Pending recordings require .recording.mp4 and .mp4 paths.");
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                Path.GetDirectoryName(normalizedTemporaryPath),
                Path.GetDirectoryName(normalizedFinalPath),
                comparison))
        {
            throw new ArgumentException(
                "Pending recording paths must share one directory.");
        }

        this.TemporaryPath = normalizedTemporaryPath;
        this.FinalPath = normalizedFinalPath;
    }

    public string TemporaryPath { get; }

    public string FinalPath { get; }
}
