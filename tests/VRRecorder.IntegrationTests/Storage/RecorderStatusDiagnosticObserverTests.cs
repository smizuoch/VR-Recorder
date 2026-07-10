using System.Text.Json;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Recording;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class RecorderStatusDiagnosticObserverTests
{
    [Fact]
    public async Task RecordsReplayAndTransitionsInRevisionOrderUntilDisposed()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(directory.Path);
        using var statuses = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        var observer = new RecorderStatusDiagnosticObserver(
            statuses,
            log,
            new IncrementingWallClock());

        statuses.TryPublish(RecorderStatusSnapshot.Create(
            1,
            RecorderState.Arming));
        statuses.TryPublish(RecorderStatusSnapshot.Create(
            2,
            RecorderState.Recording));
        observer.Dispose();
        statuses.TryPublish(RecorderStatusSnapshot.Create(
            3,
            RecorderState.Stopping));

        var lines = await File.ReadAllLinesAsync(Path.Combine(
            directory.Path,
            "vr-recorder.jsonl"));
        Assert.Equal(3, lines.Length);
        var transitions = lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Equal(
                "recording.state_transition",
                root.GetProperty("event").GetString());
            var fields = root.GetProperty("fields");
            return (
                Revision: long.Parse(
                    fields.GetProperty("revision").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture),
                State: fields.GetProperty("state").GetString()!);
        }).ToArray();
        Assert.Equal(
            new[]
            {
                (0L, "ready"),
                (1L, "arming"),
                (2L, "recording"),
            },
            transitions);
    }

    private sealed class IncrementingWallClock : IWallClock
    {
        private long _ticks;

        public DateTimeOffset LocalNow =>
            DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Increment(ref _ticks));
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
                $"vr-recorder-status-log-tests-{Guid.NewGuid():N}");
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
