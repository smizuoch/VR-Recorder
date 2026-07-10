using System.Globalization;
using System.Text.Json;

namespace VRRecorder.Infrastructure.Storage;

public sealed class RotatingJsonLinesDiagnosticLog : IDisposable
{
    public const long DefaultMaximumFileBytes = 10L * 1024 * 1024;
    public const int DefaultMaximumFileCount = 5;
    private const string ActiveFileName = "vr-recorder.jsonl";
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private readonly int _maximumFileCount;
    private bool _disposed;

    public RotatingJsonLinesDiagnosticLog(string directory)
        : this(
            directory,
            DefaultMaximumFileBytes,
            DefaultMaximumFileCount)
    {
    }

    public RotatingJsonLinesDiagnosticLog(
        string directory,
        long maximumFileBytes,
        int maximumFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException(
                "The diagnostic log directory must be absolute.",
                nameof(directory));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileCount);
        _directory = Path.GetFullPath(directory);
        _maximumFileBytes = maximumFileBytes;
        _maximumFileCount = maximumFileCount;
    }

    public Task WriteAsync(
        DiagnosticLogEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        var line = SerializeLine(entry);
        if (line.LongLength > _maximumFileBytes)
        {
            throw new InvalidDataException(
                "The diagnostic event exceeds the configured log-file limit.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDirectory();
            var activePath = ActivePath();
            EnsureRegularFileOrMissing(activePath);
            if (File.Exists(activePath) &&
                new FileInfo(activePath).Length > _maximumFileBytes - line.Length)
            {
                Rotate();
            }

            using var stream = new FileStream(
                activePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            stream.Write(line);
            stream.Flush(flushToDisk: true);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private static byte[] SerializeLine(DiagnosticLogEntry entry)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            timestampUtc = entry.TimestampUtc.ToString(
                "O",
                CultureInfo.InvariantCulture),
            level = LevelName(entry.Level),
            @event = entry.EventName,
            fields = entry.Fields,
        });
        var line = new byte[json.Length + 1];
        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        return line;
    }

    private static string LevelName(DiagnosticLogLevel level) => level switch
    {
        DiagnosticLogLevel.Information => "information",
        DiagnosticLogLevel.Warning => "warning",
        DiagnosticLogLevel.Error => "error",
        _ => throw new ArgumentOutOfRangeException(
            nameof(level),
            level,
            "The diagnostic log level is not supported."),
    };

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_directory);
        if ((File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The diagnostic log directory cannot be a reparse point.");
        }
    }

    private void Rotate()
    {
        for (var ordinal = _maximumFileCount - 1; ordinal >= 1; ordinal--)
        {
            var destination = ArchivePath(ordinal);
            EnsureRegularFileOrMissing(destination);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            var source = ordinal == 1
                ? ActivePath()
                : ArchivePath(ordinal - 1);
            EnsureRegularFileOrMissing(source);
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        if (_maximumFileCount == 1 && File.Exists(ActivePath()))
        {
            File.Delete(ActivePath());
        }
    }

    private static void EnsureRegularFileOrMissing(string path)
    {
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "A diagnostic log file cannot be a reparse point.");
        }
    }

    private string ActivePath() => Path.Combine(_directory, ActiveFileName);

    private string ArchivePath(int ordinal) => Path.Combine(
        _directory,
        $"vr-recorder.{ordinal.ToString(CultureInfo.InvariantCulture)}.jsonl");
}
