using System.Text.Json;
using VRRecorder.Application.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class FfprobeRecordingFileValidatorTests
{
    [Fact]
    public async Task OneValidatorUsesTheExpectationOfEachRecordingSession()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var ffprobePath = Path.Combine(directory.Path, "ffprobe-fixture");
        var recordingPath = Path.Combine(directory.Path, "recording.mp4");
        await File.WriteAllBytesAsync(recordingPath, []);
        await File.WriteAllTextAsync(
            ffprobePath,
            CreateProbeFixtureScript("30/1"));
        File.SetUnixFileMode(
            ffprobePath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        var validator = new FfprobeRecordingFileValidator(ffprobePath);
        var recording = new FinalizedRecording(recordingPath);
        var matching = new RecordingMediaExpectation(
            Width: 320,
            Height: 180,
            FramesPerSecond: 30,
            AudioSampleRate: 48000,
            AudioChannels: 2,
            ExpectedDuration: TimeSpan.FromSeconds(3));
        var mismatching = matching with { Width = 640 };

        var valid = await validator.ValidateAsync(
            recording,
            matching,
            CancellationToken.None);
        var invalid = await validator.ValidateAsync(
            recording,
            mismatching,
            CancellationToken.None);

        Assert.Equal(RecordingFileValidation.Valid, valid);
        Assert.Equal(RecordingFileValidation.Invalid, invalid);
    }

    [Theory]
    [InlineData("0/0")]
    [InlineData("NaN/1")]
    [InlineData("Infinity/Infinity")]
    public async Task NonFiniteFrameRateIsInvalid(string frameRate)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var ffprobePath = Path.Combine(directory.Path, "ffprobe-fixture");
        var recordingPath = Path.Combine(directory.Path, "recording.mp4");
        await File.WriteAllBytesAsync(recordingPath, []);
        await File.WriteAllTextAsync(
            ffprobePath,
            CreateProbeFixtureScript(frameRate));
        File.SetUnixFileMode(
            ffprobePath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        var validator = new FfprobeRecordingFileValidator(
            ffprobePath,
            new RecordingMediaExpectation(
                Width: 320,
                Height: 180,
                FramesPerSecond: 30,
                AudioSampleRate: 48000,
                AudioChannels: 2,
                ExpectedDuration: TimeSpan.FromSeconds(3)));

        var result = await validator.ValidateAsync(
            new FinalizedRecording(recordingPath),
            CancellationToken.None);

        Assert.Equal(RecordingFileValidation.Invalid, result);
    }

    private static string CreateProbeFixtureScript(string frameRate)
    {
        var json = JsonSerializer.Serialize(new
        {
            streams = new object[]
            {
                new
                {
                    codec_type = "video",
                    codec_name = "h264",
                    width = 320,
                    height = 180,
                    r_frame_rate = frameRate,
                },
                new
                {
                    codec_type = "audio",
                    codec_name = "aac",
                    sample_rate = "48000",
                    channels = 2,
                },
            },
            format = new
            {
                duration = "3.000000",
            },
        });
        return $"#!/bin/sh\nprintf '%s\\n' '{json}'\n";
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
                $"vr-recorder-ffprobe-tests-{Guid.NewGuid():N}");
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
