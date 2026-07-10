using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class FfprobeRecordingFileValidator : IRecordingFileValidator
{
    private readonly string _ffprobePath;
    private readonly RecordingMediaExpectation _expectation;

    public FfprobeRecordingFileValidator(
        string ffprobePath,
        RecordingMediaExpectation expectation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        ArgumentNullException.ThrowIfNull(expectation);
        if (!Path.IsPathFullyQualified(ffprobePath))
        {
            throw new ArgumentException(
                "The ffprobe path must be absolute.",
                nameof(ffprobePath));
        }

        _ffprobePath = ffprobePath;
        _expectation = expectation;
    }

    public async Task<RecordingFileValidation> ValidateAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        try
        {
            var probe = await RunProbeAsync(
                    recording.FinalPath,
                    cancellationToken)
                .ConfigureAwait(false);
            return probe.ExitCode == 0 && MatchesExpectation(probe.StandardOutput)
                ? RecordingFileValidation.Valid
                : RecordingFileValidation.Invalid;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                Win32Exception or
                JsonException or
                InvalidOperationException or
                KeyNotFoundException or
                FormatException or
                OverflowException)
        {
            return RecordingFileValidation.Invalid;
        }
    }

    private bool MatchesExpectation(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var videoStreams = streams.Where(stream =>
            string.Equals(
                stream.GetProperty("codec_type").GetString(),
                "video",
                StringComparison.Ordinal)).ToArray();
        var audioStreams = streams.Where(stream =>
            string.Equals(
                stream.GetProperty("codec_type").GetString(),
                "audio",
                StringComparison.Ordinal)).ToArray();
        if (videoStreams.Length != 1 || audioStreams.Length != 1)
        {
            return false;
        }

        var video = videoStreams[0];
        var audio = audioStreams[0];
        if (!string.Equals(
                video.GetProperty("codec_name").GetString(),
                "h264",
                StringComparison.Ordinal) ||
            video.GetProperty("width").GetInt32() != _expectation.Width ||
            video.GetProperty("height").GetInt32() != _expectation.Height ||
            Math.Abs(ReadFrameRate(video) - _expectation.FramesPerSecond) > 0.001 ||
            !string.Equals(
                audio.GetProperty("codec_name").GetString(),
                "aac",
                StringComparison.Ordinal) ||
            int.Parse(
                audio.GetProperty("sample_rate").GetString()!,
                CultureInfo.InvariantCulture) != _expectation.AudioSampleRate ||
            audio.GetProperty("channels").GetInt32() != _expectation.AudioChannels)
        {
            return false;
        }

        if (_expectation.ExpectedDuration is not { } expectedDuration)
        {
            return true;
        }

        var actualDuration = double.Parse(
            root.GetProperty("format").GetProperty("duration").GetString()!,
            CultureInfo.InvariantCulture);
        return Math.Abs(actualDuration - expectedDuration.TotalSeconds) <= 0.1;
    }

    private static double ReadFrameRate(JsonElement video)
    {
        var parts = video
            .GetProperty("r_frame_rate")
            .GetString()!
            .Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new FormatException("The video frame rate is not rational.");
        }

        var numerator = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var denominator = double.Parse(parts[1], CultureInfo.InvariantCulture);
        return numerator / denominator;
    }

    private async Task<ProbeResult> RunProbeAsync(
        string recordingPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_ffprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        string[] arguments =
        [
            "-v", "error", "-show_entries",
            "format=duration:stream=codec_type,codec_name,width,height,r_frame_rate,sample_rate,channels",
            "-of", "json", "-i", recordingPath,
        ];
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException(
                                "Could not start the configured ffprobe process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(
            cancellationToken);
        try
        {
            await process
                .WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process
                .WaitForExitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        return new ProbeResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private sealed record ProbeResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
