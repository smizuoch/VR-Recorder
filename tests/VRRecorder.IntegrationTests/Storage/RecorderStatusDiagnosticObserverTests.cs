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

    [Fact]
    public async Task RecordsEveryStateNameAndSeverity()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(directory.Path);
        var states = Enum.GetValues<RecorderState>();
        using var statuses = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, states[0]));
        using var observer = new RecorderStatusDiagnosticObserver(
            statuses,
            log,
            new IncrementingWallClock());
        for (var index = 1; index < states.Length; index++)
        {
            Assert.True(statuses.TryPublish(RecorderStatusSnapshot.Create(
                index,
                states[index])));
        }

        observer.Dispose();

        var actual = (await File.ReadAllLinesAsync(Path.Combine(
                directory.Path,
                "vr-recorder.jsonl")))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                return (
                    State: root.GetProperty("fields")
                        .GetProperty("state")
                        .GetString()!,
                    Level: root.GetProperty("level").GetString()!);
            })
            .ToArray();
        Assert.Equal(
            new[]
            {
                ("booting", "information"),
                ("compliance_fault", "error"),
                ("ready", "information"),
                ("arming", "information"),
                ("countdown", "information"),
                ("starting", "information"),
                ("recording", "information"),
                ("signal_lost", "warning"),
                ("stopping", "information"),
                ("no_signal", "warning"),
                ("faulted", "error"),
            },
            actual);
    }

    [Fact]
    public void DisposalIsIdempotentAndRejectsLateStatusCallback()
    {
        using var directory = TemporaryDirectory.Create();
        using var log = new RotatingJsonLinesDiagnosticLog(directory.Path);
        var statuses = new CallbackOnDisposeStatusSource();
        var observer = new RecorderStatusDiagnosticObserver(
            statuses,
            log,
            new IncrementingWallClock());

        observer.Dispose();
        observer.Dispose();

        var lines = File.ReadAllLines(Path.Combine(
            directory.Path,
            "vr-recorder.jsonl"));
        Assert.Single(lines);
        Assert.Contains("\"state\":\"ready\"", lines[0]);
    }

    [Fact]
    public void DiagnosticWriteFailureDoesNotEscapeDisposal()
    {
        using var statuses = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        using var observer = new RecorderStatusDiagnosticObserver(
            statuses,
            new ThrowingDiagnosticLogWriter(),
            new IncrementingWallClock());

        observer.Dispose();
    }

    [Fact]
    public void RejectsUnsupportedRecorderStateName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecorderStatusDiagnosticObserver.StateName(
                (RecorderState)(-1)));
    }

    private sealed class IncrementingWallClock : IWallClock
    {
        private long _ticks;

        public DateTimeOffset LocalNow =>
            DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Increment(ref _ticks));
    }

    private sealed class CallbackOnDisposeStatusSource : IRecorderStatusSource
    {
        public RecorderStatusSnapshot Current { get; } =
            RecorderStatusSnapshot.Create(0, RecorderState.Ready);

        public IDisposable Subscribe(Action<RecorderStatusSnapshot> subscriber)
        {
            subscriber(Current);
            return new CallbackSubscription(() => subscriber(
                RecorderStatusSnapshot.Create(1, RecorderState.Stopping)));
        }
    }

    private sealed class CallbackSubscription(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose()
        {
            Interlocked.Exchange(ref _callback, null)?.Invoke();
        }
    }

    private sealed class ThrowingDiagnosticLogWriter : IDiagnosticLogWriter
    {
        public Task WriteAsync(
            DiagnosticLogEntry entry,
            CancellationToken cancellationToken) =>
            Task.FromException(new IOException(
                "diagnostic storage unavailable"));
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
