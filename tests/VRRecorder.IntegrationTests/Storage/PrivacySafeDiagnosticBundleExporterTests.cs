using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class PrivacySafeDiagnosticBundleExporterTests
{
    [Theory]
    [MemberData(nameof(UnsafeDiagnosticLines))]
    public void RejectsMalformedOrPrivacyUnsafeDiagnosticLine(string line)
    {
        Assert.Null(PrivacySafeDiagnosticBundleExporter.TrySanitize(line));
    }

    public static IEnumerable<object[]> UnsafeDiagnosticLines()
    {
        yield return [string.Empty];
        yield return [new string('x', (64 * 1024) + 1)];
        yield return ["[]"];
        yield return ["""
            {
              "timestampUtc":"2026-07-11T01:02:03.0000000+00:00",
              "timestampUtc":"2026-07-11T01:02:03.0000000+00:00",
              "level":"information",
              "event":"recording.saved",
              "fields":{"container":"mp4","result":"saved"}
            }
            """];
        yield return [Envelope(
            "recording.saved",
            Fields(("container", "mp4"), ("result", "saved")),
            timestamp: "invalid")];
        yield return [Envelope(
            "recording.saved",
            Fields(("container", "mp4"), ("result", "saved")),
            timestamp: "2026-07-11T01:02:03.0000000+09:00")];
        yield return [Envelope(
            "recording.saved",
            Fields(("container", "mp4"), ("result", "saved")),
            level: "debug")];
        yield return [Envelope("unknown.event", Fields(("secret", "value")))];
        yield return [Envelope("recording.saved", new object[] { "not", "object" })];
        yield return ["""
            {
              "timestampUtc":"2026-07-11T01:02:03.0000000+00:00",
              "level":"information",
              "event":"recording.saved",
              "fields":{"container":"mp4","container":"mp4","result":"saved"}
            }
            """];

        foreach (var line in Mutations(
                     "recording.state_transition",
                     Fields(("revision", "7"), ("state", "recording")),
                     ("revision", null, true),
                     ("revision", "invalid", false),
                     ("revision", "-1", false),
                     ("state", "private-state", false)))
            yield return [line];

        foreach (var line in Mutations(
                     "recording.storage",
                     Fields(
                         ("availableBytes", "1024"),
                         ("estimatedRemainingSeconds", "12.5"),
                         ("state", "healthy")),
                     ("availableBytes", null, true),
                     ("availableBytes", "invalid", false),
                     ("availableBytes", "-1", false),
                     ("estimatedRemainingSeconds", null, true),
                     ("estimatedRemainingSeconds", "invalid", false),
                     ("estimatedRemainingSeconds", "-1", false),
                     ("state", "private-state", false)))
            yield return [line];

        foreach (var line in Mutations(
                     "recording.saved",
                     Fields(("container", "mp4"), ("result", "saved")),
                     ("container", null, true),
                     ("container", "mkv", false),
                     ("result", null, true),
                     ("result", "private-result", false)))
            yield return [line];

        foreach (var line in Mutations(
                     "camera.restore_warning",
                     Fields(("reason", "recording_completed")),
                     ("reason", null, true),
                     ("reason", "private-reason", false)))
            yield return [line];

        foreach (var eventCase in new[]
                 {
                     (
                         Event: "audio.input_warning",
                         Kind: "input_unavailable"),
                     (
                         Event: "audio.input_status",
                         Kind: "input_recovered"),
                     (
                         Event: "audio.buffer_health",
                         Kind: "buffer_overrun"),
                 })
        {
            foreach (var line in Mutations(
                         eventCase.Event,
                         Fields(
                             ("framePosition", "4800"),
                             ("input", "microphone"),
                             ("kind", eventCase.Kind)),
                         ("framePosition", null, true),
                         ("framePosition", "invalid", false),
                         ("framePosition", "-1", false),
                         ("input", "private-input", false),
                         ("kind", "private-kind", false)))
                yield return [line];
        }

        foreach (var line in Mutations(
                     "audio.input_status",
                     Fields(
                         ("framePosition", "4800"),
                         ("input", "microphone"),
                         ("kind", "input_recovered"),
                         ("rediscoveryBudgetMilliseconds", "10.5")),
                     ("rediscoveryBudgetMilliseconds", 1, false),
                     ("rediscoveryBudgetMilliseconds", "invalid", false),
                     ("rediscoveryBudgetMilliseconds", "-1", false)))
            yield return [line];

        foreach (var line in Mutations(
                     "recording.media_profile",
                     Fields(
                         ("encoder", "nvenc"),
                         ("estimatedSourceFramesPerSecond", "59.94"),
                         ("gpuVendor", "nvidia"),
                         ("outputFramesPerSecond", "60"),
                         ("outputHeight", "1080"),
                         ("outputWidth", "1920"),
                         ("sourceHeight", "1080"),
                         ("sourcePixelFormat", "bgra8"),
                         ("sourceWidth", "1920")),
                     ("encoder", "private", false),
                     ("estimatedSourceFramesPerSecond", "0", false),
                     ("estimatedSourceFramesPerSecond", "1001", false),
                     ("gpuVendor", "private", false),
                     ("outputFramesPerSecond", "0", false),
                     ("outputFramesPerSecond", "1001", false),
                     ("outputHeight", "0", false),
                     ("outputWidth", "invalid", false),
                     ("sourceHeight", "-1", false),
                     ("sourcePixelFormat", "private", false),
                     ("sourceWidth", null, true)))
            yield return [line];

        foreach (var line in Mutations(
                     "recording.media_statistics",
                     Fields(
                         ("audioVideoOffsetMicroseconds", "-10"),
                         ("droppedSourceVideoFrameCount", "1"),
                         ("duplicatedOutputVideoFrameCount", "1"),
                         ("latestEncodeLatencyMicroseconds", "10"),
                         ("maximumEncodeLatencyMicroseconds", "20"),
                         ("muxedAudioPacketCount", "1"),
                         ("muxedVideoPacketCount", "1"),
                         ("sourceVideoFrameCount", "1")),
                     ("audioVideoOffsetMicroseconds", null, true),
                     ("audioVideoOffsetMicroseconds", "invalid", false),
                     ("droppedSourceVideoFrameCount", "-1", false),
                     ("duplicatedOutputVideoFrameCount", "invalid", false),
                     ("latestEncodeLatencyMicroseconds", "-1", false),
                     ("maximumEncodeLatencyMicroseconds", "5", false),
                     ("muxedAudioPacketCount", "-1", false),
                     ("muxedVideoPacketCount", "invalid", false),
                     ("sourceVideoFrameCount", null, true)))
            yield return [line];

        foreach (var line in Mutations(
                     "application.environment",
                     Fields(
                         ("appVersion", "0.3.0"),
                         ("architecture", "x64"),
                         ("gpuModel", "ven_10de&dev_2684"),
                         ("gpuVendor", "nvidia"),
                         ("osBuild", "10.0.26100"),
                         ("driverVersion", "32.0.15.6094")),
                     ("appVersion", null, true),
                     ("appVersion", "1.2", false),
                     ("appVersion", "1.2.3.4.5", false),
                     ("appVersion", "1..3", false),
                     ("appVersion", "01.2.3", false),
                     ("appVersion", "a.2.3", false),
                     ("architecture", "private", false),
                     ("gpuVendor", "private", false),
                     ("gpuModel", null, true),
                     ("gpuModel", "private", false),
                     ("gpuModel", "ven_10de&bad_2684", false),
                     ("gpuModel", "ven_10d&dev_2684", false),
                     ("gpuModel", "ven_zzzz&dev_2684", false),
                     ("osBuild", "10.0", false),
                     ("driverVersion", "32.0.15", false)))
            yield return [line];

        foreach (var line in Mutations(
                     "recording.finalization_recovery",
                     Fields(("reason", "validation_failed")),
                     ("reason", "private", false)))
            yield return [line];

        foreach (var line in Mutations(
                     "osc.operation",
                     Fields(
                         ("operation", "capability_probe"),
                         ("outcome", "succeeded")),
                     ("operation", "private", false),
                     ("outcome", "private", false)))
            yield return [line];
    }

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
                    "audio.buffer_health",
                    new Dictionary<string, string>
                    {
                        ["input"] = "microphone",
                        ["kind"] = "buffer_overrun",
                        ["framePosition"] = "24480",
                        ["endpointId"] = "private-health-endpoint-secret",
                        ["samples"] = "private-audio-samples-secret",
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
        Assert.Equal(11, result.EventCount);
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
        Assert.Contains("audio.buffer_health", diagnostics);
        Assert.Contains("\"kind\":\"buffer_overrun\"", diagnostics);
        Assert.Contains("\"framePosition\":\"24480\"", diagnostics);
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
                     "private-health-endpoint-secret",
                     "private-audio-samples-secret",
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
        Assert.Equal(11, lines.Length);
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

    [Fact]
    public void SanitizesValidStorageAndOptionalAudioBudget()
    {
        var storage = PrivacySafeDiagnosticBundleExporter.TrySanitize(
            Envelope(
                "recording.storage",
                Fields(
                    ("availableBytes", "1024"),
                    ("estimatedRemainingSeconds", "12.500"),
                    ("state", "healthy"))));
        var audio = PrivacySafeDiagnosticBundleExporter.TrySanitize(
            Envelope(
                "audio.input_status",
                Fields(
                    ("framePosition", "4800"),
                    ("input", "microphone"),
                    ("kind", "input_recovered"),
                    ("rediscoveryBudgetMilliseconds", "10.500"))));

        Assert.Contains("\"estimatedRemainingSeconds\":\"12.5\"", storage);
        Assert.Contains(
            "\"rediscoveryBudgetMilliseconds\":\"10.5\"",
            audio);
    }

    [Fact]
    public async Task MissingLogDirectoryProducesEmptyBundle()
    {
        using var root = TemporaryDirectory.Create("missing-log-root");
        using var output = TemporaryDirectory.Create("missing-log-output");
        var exporter = new PrivacySafeDiagnosticBundleExporter(Path.Combine(
            root.Path,
            "missing"));
        var destination = Path.Combine(output.Path, "diagnostics.zip");

        var result = await exporter.ExportAsync(
            destination,
            CancellationToken.None);

        Assert.Equal(0, result.EventCount);
        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal(
            string.Empty,
            await ReadEntryAsync(archive.GetEntry("diagnostics.jsonl")!));
    }

    [Fact]
    public async Task RejectsRelativeLogAndDestinationPaths()
    {
        Assert.Throws<ArgumentException>(() =>
            new PrivacySafeDiagnosticBundleExporter("diagnostics"));

        using var logs = TemporaryDirectory.Create("relative-logs");
        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => exporter.ExportAsync(
            "diagnostics.zip",
            CancellationToken.None));
    }

    [Fact]
    public async Task RejectsMissingOutputDirectoryAndDirectoryDestination()
    {
        using var logs = TemporaryDirectory.Create("invalid-output-logs");
        using var output = TemporaryDirectory.Create("invalid-output");
        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            exporter.ExportAsync(
                Path.Combine(output.Path, "missing", "diagnostics.zip"),
                CancellationToken.None));
        var directoryDestination = Path.Combine(
            output.Path,
            "diagnostics.zip");
        Directory.CreateDirectory(directoryDestination);

        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(
            directoryDestination,
            CancellationToken.None));
    }

    [Fact]
    public async Task RejectsLinkedOutputDirectory()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var logs = TemporaryDirectory.Create("linked-output-logs");
        using var output = TemporaryDirectory.Create("linked-output-root");
        var target = Path.Combine(output.Path, "target");
        var link = Path.Combine(output.Path, "link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);

        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(
            Path.Combine(link, "diagnostics.zip"),
            CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public async Task RejectsLinkedDestinationWithoutChangingTarget()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var logs = TemporaryDirectory.Create("linked-destination-logs");
        using var output = TemporaryDirectory.Create("linked-destination-output");
        using var outside = TemporaryDirectory.Create("linked-destination-target");
        var target = Path.Combine(outside.Path, "outside.zip");
        var destination = Path.Combine(output.Path, "diagnostics.zip");
        await File.WriteAllTextAsync(target, "outside evidence");
        File.CreateSymbolicLink(destination, target);
        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);

        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(
            destination,
            CancellationToken.None));

        Assert.Equal("outside evidence", await File.ReadAllTextAsync(target));
        Assert.Equal(target, new FileInfo(destination).LinkTarget);
    }

    [Fact]
    public async Task RejectsOversizedLogBeforeCreatingBundle()
    {
        using var logs = TemporaryDirectory.Create("oversized-logs");
        using var output = TemporaryDirectory.Create("oversized-output");
        var logPath = Path.Combine(logs.Path, "vr-recorder.jsonl");
        await using (var stream = new FileStream(
                         logPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            stream.SetLength(
                RotatingJsonLinesDiagnosticLog.DefaultMaximumFileBytes + 1);
        }

        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            exporter.ExportAsync(
                Path.Combine(output.Path, "diagnostics.zip"),
                CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    [Fact]
    public async Task InvalidUtf8RemovesTemporaryBundle()
    {
        using var logs = TemporaryDirectory.Create("invalid-utf8-logs");
        using var output = TemporaryDirectory.Create("invalid-utf8-output");
        await File.WriteAllBytesAsync(
            Path.Combine(logs.Path, "vr-recorder.jsonl"),
            [0xff]);
        var exporter = new PrivacySafeDiagnosticBundleExporter(logs.Path);

        await Assert.ThrowsAsync<DecoderFallbackException>(() =>
            exporter.ExportAsync(
                Path.Combine(output.Path, "diagnostics.zip"),
                CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
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

    private static Dictionary<string, object?> Fields(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(
            value => value.Name,
            value => value.Value,
            StringComparer.Ordinal);

    private static IEnumerable<string> Mutations(
        string eventName,
        IReadOnlyDictionary<string, object?> validFields,
        params (string Name, object? Value, bool Remove)[] mutations)
    {
        foreach (var mutation in mutations)
        {
            var fields = new Dictionary<string, object?>(
                validFields,
                StringComparer.Ordinal);
            if (mutation.Remove)
            {
                fields.Remove(mutation.Name);
            }
            else
            {
                fields[mutation.Name] = mutation.Value;
            }

            yield return Envelope(eventName, fields);
        }
    }

    private static string Envelope(
        string eventName,
        object fields,
        string timestamp = "2026-07-11T01:02:03.0000000+00:00",
        string level = "information") =>
        JsonSerializer.Serialize(new
        {
            timestampUtc = timestamp,
            level,
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
