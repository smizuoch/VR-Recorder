namespace VRRecorder.IntegrationTests;

internal sealed record HostFfmpegTools(
    string FfmpegPath,
    string FfprobePath)
{
    private const string FfmpegEnvironmentVariable =
        "VRRECORDER_TEST_FFMPEG";
    private const string FfprobeEnvironmentVariable =
        "VRRECORDER_TEST_FFPROBE";

    public static HostFfmpegTools Resolve()
    {
        var configuredFfmpeg = Environment.GetEnvironmentVariable(
            FfmpegEnvironmentVariable);
        var configuredFfprobe = Environment.GetEnvironmentVariable(
            FfprobeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredFfmpeg) ||
            !string.IsNullOrWhiteSpace(configuredFfprobe))
        {
            if (string.IsNullOrWhiteSpace(configuredFfmpeg) ||
                string.IsNullOrWhiteSpace(configuredFfprobe))
            {
                throw new InvalidOperationException(
                    $"{FfmpegEnvironmentVariable} and " +
                    $"{FfprobeEnvironmentVariable} must be set together.");
            }

            return RequireExistingPair(
                configuredFfmpeg,
                configuredFfprobe);
        }

        var ffmpegFileName = OperatingSystem.IsWindows()
            ? "ffmpeg.exe"
            : "ffmpeg";
        var ffprobeFileName = OperatingSystem.IsWindows()
            ? "ffprobe.exe"
            : "ffprobe";
        foreach (var directory in CandidateDirectories())
        {
            var ffmpegPath = Path.Combine(directory, ffmpegFileName);
            var ffprobePath = Path.Combine(directory, ffprobeFileName);
            if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
            {
                return new HostFfmpegTools(ffmpegPath, ffprobePath);
            }
        }

        throw new InvalidOperationException(
            "A colocated ffmpeg and ffprobe pair was not found. Set " +
            $"{FfmpegEnvironmentVariable} and {FfprobeEnvironmentVariable}.");
    }

    private static HostFfmpegTools RequireExistingPair(
        string ffmpegPath,
        string ffprobePath)
    {
        if (!Path.IsPathFullyQualified(ffmpegPath) ||
            !Path.IsPathFullyQualified(ffprobePath))
        {
            throw new InvalidOperationException(
                "Configured host media tool paths must be absolute.");
        }

        var canonicalFfmpeg = Path.GetFullPath(ffmpegPath);
        var canonicalFfprobe = Path.GetFullPath(ffprobePath);
        if (!File.Exists(canonicalFfmpeg) || !File.Exists(canonicalFfprobe))
        {
            throw new InvalidOperationException(
                "The configured host media tool pair does not exist.");
        }

        return new HostFfmpegTools(canonicalFfmpeg, canonicalFfprobe);
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield return "/usr/bin";
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var segment in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            var directory = segment.Trim('"');
            if (Path.IsPathFullyQualified(directory))
            {
                yield return Path.GetFullPath(directory);
            }
        }
    }
}
