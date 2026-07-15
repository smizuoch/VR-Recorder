using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Media;

public sealed class RealMp4FinalizationIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-004")]
    public async Task SyntheticAvIsProbedBeforeFinalRenameIsPublished()
    {
        var tools = HostFfmpegTools.Resolve();
        using var directory = TemporaryDirectory.Create();
        var temporaryPath = Path.Combine(directory.Path, "take.recording.mp4");
        var finalPath = Path.Combine(directory.Path, "take.mp4");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await GenerateSyntheticRecordingAsync(
            tools.FfmpegPath,
            temporaryPath,
            timeout.Token);
        var expectation = new RecordingMediaExpectation(
            Width: 320,
            Height: 180,
            FramesPerSecond: 30,
            AudioSampleRate: 48000,
            AudioChannels: 2,
            ExpectedDuration: TimeSpan.FromSeconds(3));
        var savedSink = new CountingSavedSink();
        var useCase = new RecordingFileFinalizationUseCase(
            new SameDirectoryAtomicRecordingFileFinalizer(),
            new FfprobeRecordingFileValidator(tools.FfprobePath, expectation),
            new FileSystemRecordingRecoveryStore(),
            savedSink);

        var result = await useCase.ExecuteAsync(
            new PendingRecording(temporaryPath, finalPath),
            timeout.Token);

        var saved = Assert.IsType<RecordingFinalizationResult.Saved>(result);
        Assert.Equal(Path.GetFullPath(finalPath), saved.Recording.FinalPath);
        Assert.Equal(1, savedSink.CallCount);
        Assert.False(File.Exists(temporaryPath));
        using var probe = await ProbeAsync(
            tools.FfprobePath,
            finalPath,
            timeout.Token);
        AssertPlayableMedia(probe.RootElement);
    }

    private static async Task GenerateSyntheticRecordingAsync(
        string ffmpegPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
            "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high",
            "-pix_fmt", "yuv420p", "-g", "60", "-keyint_min", "60",
            "-sc_threshold", "0", "-bf", "0", "-threads:v", "1",
            "-fps_mode", "cfr", "-c:a", "aac", "-profile:a", "aac_low",
            "-b:a", "192k", "-ar", "48000", "-ac", "2", "-t", "3",
            "-shortest", "-map_metadata", "-1", "-metadata",
            "creation_time=1970-01-01T00:00:00Z", "-movflags",
            "+frag_keyframe+empty_moov+default_base_moof", "-frag_duration",
            "1000000", "-flush_packets", "1", "-f", "mp4", outputPath,
        ];
        var result = await RunProcessAsync(
            ffmpegPath,
            arguments,
            cancellationToken);
        Assert.True(
            result.ExitCode == 0,
            $"ffmpeg failed with {result.ExitCode}: {result.StandardError}");
    }

    private static async Task<JsonDocument> ProbeAsync(
        string ffprobePath,
        string recordingPath,
        CancellationToken cancellationToken)
    {
        string[] arguments =
        [
            "-v", "error", "-count_packets", "-show_entries",
            "format=format_name,duration:stream=index,codec_type,codec_name,profile,width,height,pix_fmt,r_frame_rate,sample_rate,channels,nb_read_packets",
            "-of", "json", "-i", recordingPath,
        ];
        var result = await RunProcessAsync(
            ffprobePath,
            arguments,
            cancellationToken);
        Assert.True(
            result.ExitCode == 0,
            $"ffprobe failed with {result.ExitCode}: {result.StandardError}");
        return JsonDocument.Parse(result.StandardOutput);
    }

    private static void AssertPlayableMedia(JsonElement root)
    {
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var video = Assert.Single(streams, stream =>
            stream.GetProperty("codec_type").GetString() == "video");
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal("yuv420p", video.GetProperty("pix_fmt").GetString());
        Assert.Equal(320, video.GetProperty("width").GetInt32());
        Assert.Equal(180, video.GetProperty("height").GetInt32());
        Assert.Equal("30/1", video.GetProperty("r_frame_rate").GetString());
        Assert.Equal("90", video.GetProperty("nb_read_packets").GetString());

        var audio = Assert.Single(streams, stream =>
            stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.Equal("48000", audio.GetProperty("sample_rate").GetString());
        Assert.Equal(2, audio.GetProperty("channels").GetInt32());
        Assert.True(int.Parse(
            audio.GetProperty("nb_read_packets").GetString()!,
            CultureInfo.InvariantCulture) > 0);

        var duration = double.Parse(
            root.GetProperty("format").GetProperty("duration").GetString()!,
            CultureInfo.InvariantCulture);
        Assert.InRange(duration, 3.0, 3.1);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executablePath,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException(
                                $"Could not start {executablePath}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(
            cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class CountingSavedSink : ISavedRecordingSink
    {
        public int CallCount { get; private set; }

        public Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-media-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
