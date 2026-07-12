using System.IO.Compression;
using System.Text.Json;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class PrivacySafeDiagnosticBundleExporterTests
{
    [Fact]
    public async Task ExportsOnlySanitizedKnownDiagnosticEvents()
    {
        using var logs = TemporaryDirectory.Create("logs");
        using var output = TemporaryDirectory.Create("output");
        var privatePath = Path.Combine(
            logs.Path,
            "alice-private-world.mp4");
        await File.WriteAllLinesAsync(
            Path.Combine(logs.Path, "vr-recorder.jsonl"),
            [
                LogLine(
                    "recording.state_transition",
                    new Dictionary<string, string>
                    {
                        ["revision"] = "7",
                        ["state"] = "recording",
                        ["username"] = "alice-secret",
                    }),
                LogLine(
                    "recording.saved",
                    new Dictionary<string, string>
                    {
                        ["container"] = "mp4",
                        ["result"] = "saved",
                        ["finalPath"] = privatePath,
                    }),
                LogLine(
                    "camera.restore_warning",
                    new Dictionary<string, string>
                    {
                        ["failureType"] = "IOException",
                        ["reason"] = "recording_completed",
                        ["avatar"] = "avatar-secret",
                    }),
                LogLine(
                    "audio.input_warning",
                    new Dictionary<string, string>
                    {
                        ["failureType"] = "IOException",
                        ["framePosition"] = "4800",
                        ["input"] = "microphone",
                        ["kind"] = "input_unavailable",
                        ["endpointId"] = "private-endpoint-secret",
                    }),
                LogLine(
                    "audio.input_status",
                    new Dictionary<string, string>
                    {
                        ["framePosition"] = "9600",
                        ["input"] = "microphone",
                        ["kind"] = "input_recovered",
                        ["username"] = "audio-user-secret",
                    }),
                LogLine(
                    "recording.media_profile",
                    new Dictionary<string, string>
                    {
                        ["encoder"] = "nvenc",
                        ["estimatedSourceFramesPerSecond"] = "59.94",
                        ["gpuVendor"] = "nvidia",
                        ["outputFramesPerSecond"] = "60",
                        ["outputHeight"] = "1080",
                        ["outputWidth"] = "1920",
                        ["sourceHeight"] = "1080",
                        ["sourcePixelFormat"] = "bgra8",
                        ["sourceWidth"] = "1920",
                        ["gpuIdentity"] = "private-gpu-identity",
                    }),
                LogLine(
                    "recording.media_statistics",
                    new Dictionary<string, string>
                    {
                        ["audioVideoOffsetMicroseconds"] = "-15000",
                        ["droppedSourceVideoFrameCount"] = "30",
                        ["duplicatedOutputVideoFrameCount"] = "4",
                        ["latestEncodeLatencyMicroseconds"] = "2400",
                        ["maximumEncodeLatencyMicroseconds"] = "8000",
                        ["muxedAudioPacketCount"] = "142",
                        ["muxedVideoPacketCount"] = "90",
                        ["sourceVideoFrameCount"] = "120",
                        ["outputPath"] = privatePath,
                    }),
                LogLine(
                    "application.environment",
                    new Dictionary<string, string>
                    {
                        ["appVersion"] = "0.3.0",
                        ["architecture"] = "x64",
                        ["gpuModel"] = "ven_10de&dev_2684",
                        ["gpuVendor"] = "nvidia",
                        ["osBuild"] = "10.0.26100",
                        ["driverVersion"] = "32.0.15.6094",
                        ["computerName"] = "private-machine-secret",
                    }),
                LogLine(
                    "recording.finalization_recovery",
                    new Dictionary<string, string>
                    {
                        ["reason"] = "validation_failed",
                        ["quarantinePath"] = privatePath,
                    }),
                LogLine(
                    "osc.operation",
                    new Dictionary<string, string>
                    {
                        ["operation"] = "capability_probe",
                        ["outcome"] = "succeeded",
                        ["endpoint"] = "private-osc-endpoint-secret",
                        ["avatarValue"] = "private-avatar-value-secret",
                    }),
                LogLine(
                    "application.environment",
                    new Dictionary<string, string>
                    {
                        ["appVersion"] = "private-version-secret",
                        ["architecture"] = "x64",
                        ["gpuModel"] = "private-model-secret",
                        ["gpuVendor"] = "nvidia",
                        ["osBuild"] = "10.0.26100",
                        ["driverVersion"] = "32.0.15.6094",
                    }),
                LogLine(
                    "recording.media_profile",
                    new Dictionary<string, string>
                    {
                        ["encoder"] = "private-unknown-encoder",
                        ["estimatedSourceFramesPerSecond"] = "60",
                        ["gpuVendor"] = "nvidia",
                        ["outputFramesPerSecond"] = "60",
                        ["outputHeight"] = "1080",
                        ["outputWidth"] = "1920",
                        ["sourceHeight"] = "1080",
                        ["sourcePixelFormat"] = "bgra8",
                        ["sourceWidth"] = "1920",
                    }),
                LogLine(
                    "recording.media_statistics",
                    new Dictionary<string, string>
                    {
                        ["audioVideoOffsetMicroseconds"] = "0",
                        ["droppedSourceVideoFrameCount"] = "0",
                        ["duplicatedOutputVideoFrameCount"] = "0",
                        ["latestEncodeLatencyMicroseconds"] = "-1",
                        ["maximumEncodeLatencyMicroseconds"] = "0",
                        ["muxedAudioPacketCount"] = "0",
                        ["muxedVideoPacketCount"] = "0",
                        ["sourceVideoFrameCount"] = "0",
                    }),
                LogLine(
                    "recording.saved",
                    new Dictionary<string, string>
                    {
                        ["container"] = "mp4",
                        ["result"] = "bearer-token-secret",
                    }),
                LogLine(
                    "credential.dump",
                    new Dictionary<string, string>
                    {
                        ["token"] = "credential-secret",
                        ["world"] = "world-secret",
                    }),
                "{ unfinished private log line",
            ]);
        await File.WriteAllTextAsync(
            privatePath,
            "private video bytes and voice-secret");
        await File.WriteAllTextAsync(
            Path.Combine(logs.Path, "microphone.wav"),
            "private audio bytes");
        await File.WriteAllTextAsync(
            Path.Combine(logs.Path, "access-token.txt"),
            "credential-secret");
        var destination = Path.Combine(output.Path, "diagnostics.zip");
        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);

        var result = await exporter.ExportAsync(
            destination,
            CancellationToken.None);

        Assert.Equal(destination, result.BundlePath);
        Assert.Equal(10, result.EventCount);
        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal(
            ["README.txt", "diagnostics.jsonl"],
            archive.Entries.Select(entry => entry.FullName));
        Assert.All(archive.Entries, entry => Assert.Equal(
            new DateTime(1980, 1, 1, 0, 0, 0),
            entry.LastWriteTime.DateTime));
        var diagnostics = await ReadEntryAsync(
            archive.GetEntry("diagnostics.jsonl")!);
        Assert.Contains("recording.state_transition", diagnostics);
        Assert.Contains("recording.saved", diagnostics);
        Assert.Contains("camera.restore_warning", diagnostics);
        Assert.Contains("audio.input_warning", diagnostics);
        Assert.Contains("audio.input_status", diagnostics);
        Assert.Contains("recording.media_profile", diagnostics);
        Assert.Contains("recording.media_statistics", diagnostics);
        Assert.Contains("application.environment", diagnostics);
        Assert.Contains("recording.finalization_recovery", diagnostics);
        Assert.Contains("\"reason\":\"validation_failed\"", diagnostics);
        Assert.Contains("osc.operation", diagnostics);
        Assert.Contains("\"operation\":\"capability_probe\"", diagnostics);
        Assert.Contains("\"outcome\":\"succeeded\"", diagnostics);
        Assert.Contains("\"appVersion\":\"0.3.0\"", diagnostics);
        Assert.Contains("\"osBuild\":\"10.0.26100\"", diagnostics);
        Assert.Contains("\"driverVersion\":\"32.0.15.6094\"", diagnostics);
        Assert.Contains(
            "\"gpuModel\":\"ven_10de\\u0026dev_2684\"",
            diagnostics);
        Assert.Contains("\"encoder\":\"nvenc\"", diagnostics);
        Assert.Contains("\"sourceWidth\":\"1920\"", diagnostics);
        Assert.Contains(
            "\"droppedSourceVideoFrameCount\":\"30\"",
            diagnostics);
        Assert.Contains(
            "\"audioVideoOffsetMicroseconds\":\"-15000\"",
            diagnostics);
        foreach (var secret in new[]
                 {
                     privatePath,
                     "alice-secret",
                     "avatar-secret",
                     "private-endpoint-secret",
                     "audio-user-secret",
                     "private-gpu-identity",
                     "private-unknown-encoder",
                     "private-machine-secret",
                     "private-version-secret",
                     "private-osc-endpoint-secret",
                     "private-avatar-value-secret",
                     "bearer-token-secret",
                     "credential-secret",
                     "world-secret",
                     "voice-secret",
                     "microphone.wav",
                     "access-token.txt",
                 })
        {
            Assert.DoesNotContain(secret, diagnostics, StringComparison.Ordinal);
        }

        var lines = diagnostics.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public async Task RejectsLinkedLogWithoutLeavingPartialBundle()
    {
        using var logs = TemporaryDirectory.Create("linked-logs");
        using var outside = TemporaryDirectory.Create("outside");
        using var output = TemporaryDirectory.Create("linked-output");
        var outsideLog = Path.Combine(outside.Path, "private.jsonl");
        await File.WriteAllTextAsync(
            outsideLog,
            LogLine(
                "credential.dump",
                new Dictionary<string, string>
                {
                    ["token"] = "outside-secret",
                }));
        File.CreateSymbolicLink(
            Path.Combine(logs.Path, "vr-recorder.jsonl"),
            outsideLog);
        var destination = Path.Combine(output.Path, "diagnostics.zip");
        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);

        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(
            destination,
            CancellationToken.None));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(output.Path));
    }

    private static string LogLine(
        string eventName,
        IReadOnlyDictionary<string, string> fields) =>
        JsonSerializer.Serialize(new
        {
            timestampUtc = "2026-07-11T01:02:03.0000000+00:00",
            level = "information",
            @event = eventName,
            fields,
        });

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create(string purpose)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-{purpose}-tests-{Guid.NewGuid():N}");
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
