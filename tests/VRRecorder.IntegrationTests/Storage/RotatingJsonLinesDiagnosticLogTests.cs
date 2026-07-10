using System.Text.Json;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class RotatingJsonLinesDiagnosticLogTests
{
    [Fact]
    public async Task WritesDeterministicBomlessUtf8JsonLines()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(directory.Path);
        var entry = new DiagnosticLogEntry(
            new DateTimeOffset(2026, 7, 11, 1, 2, 3, TimeSpan.Zero),
            DiagnosticLogLevel.Information,
            "recording.saved",
            new Dictionary<string, string>
            {
                ["result"] = "saved",
                ["container"] = "mp4",
            });

        await log.WriteAsync(entry, CancellationToken.None);

        var path = Path.Combine(directory.Path, "vr-recorder.jsonl");
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.AsSpan().StartsWith(
            new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal((byte)'\n', bytes[^1]);
        using var document = JsonDocument.Parse(
            bytes.AsMemory(0, bytes.Length - 1));
        var root = document.RootElement;
        Assert.Equal(
            "2026-07-11T01:02:03.0000000+00:00",
            root.GetProperty("timestampUtc").GetString());
        Assert.Equal("information", root.GetProperty("level").GetString());
        Assert.Equal("recording.saved", root.GetProperty("event").GetString());
        Assert.Equal(
            ["container", "result"],
            root.GetProperty("fields")
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            "mp4",
            root.GetProperty("fields").GetProperty("container").GetString());
    }

    [Fact]
    public async Task RotatesBeforeLimitAndRetainsOnlyConfiguredFileCount()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(
            directory.Path,
            maximumFileBytes: 300,
            maximumFileCount: 5);

        for (var index = 0; index < 7; index++)
        {
            await log.WriteAsync(
                new DiagnosticLogEntry(
                    DateTimeOffset.UnixEpoch.AddSeconds(index),
                    DiagnosticLogLevel.Warning,
                    $"test.event-{index}",
                    new Dictionary<string, string>
                    {
                        ["detail"] = new string((char)('a' + index), 100),
                    }),
                CancellationToken.None);
        }

        var files = Directory.GetFiles(directory.Path, "vr-recorder*.jsonl")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(5, files.Length);
        Assert.All(files, file => Assert.InRange(
            new FileInfo(file).Length,
            1,
            300));
        var retainedEvents = new List<string>();
        foreach (var file in files)
        {
            foreach (var line in await File.ReadAllLinesAsync(file))
            {
                using var document = JsonDocument.Parse(line);
                retainedEvents.Add(
                    document.RootElement.GetProperty("event").GetString()!);
            }
        }

        Assert.Equal(
            [
                "test.event-2",
                "test.event-3",
                "test.event-4",
                "test.event-5",
                "test.event-6",
            ],
            retainedEvents.Order(StringComparer.Ordinal));
        Assert.False(File.Exists(Path.Combine(
            directory.Path,
            "vr-recorder.5.jsonl")));
    }

    [Fact]
    public async Task ConcurrentWritesProduceOnlyCompleteJsonLines()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(directory.Path);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(index =>
            log.WriteAsync(
                new DiagnosticLogEntry(
                    DateTimeOffset.UnixEpoch.AddMilliseconds(index),
                    DiagnosticLogLevel.Information,
                    "test.concurrent",
                    new Dictionary<string, string>
                    {
                        ["index"] = index.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    }),
                CancellationToken.None)));

        var lines = await File.ReadAllLinesAsync(Path.Combine(
            directory.Path,
            "vr-recorder.jsonl"));
        Assert.Equal(32, lines.Length);
        var indexes = lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return int.Parse(
                document.RootElement
                    .GetProperty("fields")
                    .GetProperty("index")
                    .GetString()!,
                System.Globalization.CultureInfo.InvariantCulture);
        });
        Assert.Equal(Enumerable.Range(0, 32), indexes.Order());
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
                $"vr-recorder-log-tests-{Guid.NewGuid():N}");
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
