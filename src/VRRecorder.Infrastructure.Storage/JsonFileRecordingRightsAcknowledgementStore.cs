using System.Globalization;
using System.Text.Json;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Storage;

public sealed class JsonFileRecordingRightsAcknowledgementStore
    : IRecordingRightsAcknowledgementStore, IDisposable
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };
    private readonly string _path;
    private readonly IWallClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public JsonFileRecordingRightsAcknowledgementStore(string path)
        : this(path, SystemWallClock.Instance)
    {
    }

    public JsonFileRecordingRightsAcknowledgementStore(
        string path,
        IWallClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "The recording-rights acknowledgement path must be absolute.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
        _clock = clock;
    }

    public async Task<RecordingRightsAcknowledgement?> LoadAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureRegularFileOrMissing(_path);
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(_path, cancellationToken)
                    .ConfigureAwait(false);
                return Parse(bytes);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is JsonException or
                    InvalidDataException or
                    ArgumentException or
                    FormatException or
                    OverflowException)
            {
                BackupInvalidDocument();
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        RecordingRightsAcknowledgement acknowledgement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureRegularFileOrMissing(_path);
            var directory = Path.GetDirectoryName(_path) ??
                            throw new InvalidOperationException(
                                "The acknowledgement path has no parent directory.");
            Directory.CreateDirectory(directory);
            EnsureNotReparsePoint(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.tmp-{Guid.NewGuid():N}");
            try
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(
                    new AcknowledgementDocument(
                        SchemaVersion,
                        acknowledgement.NoticeVersion,
                        acknowledgement.AcknowledgedAtUtc),
                    SerializerOptions);
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 FileOptions.Asynchronous |
                                 FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(json, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.WriteAsync(
                            new byte[] { (byte)'\n' },
                            cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static RecordingRightsAcknowledgement Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The recording-rights document must be a JSON object.");
        }

        string[] expectedProperties =
        [
            "schemaVersion",
            "noticeVersion",
            "acknowledgedAtUtc",
        ];
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != expectedProperties.Length ||
            properties.Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedProperties) is false)
        {
            throw new InvalidDataException(
                "The recording-rights document has an unexpected property set.");
        }

        var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
        if (schemaVersion != SchemaVersion)
        {
            throw new InvalidDataException(
                $"Recording-rights schema {schemaVersion} is not supported.");
        }

        var noticeVersion = root.GetProperty("noticeVersion").GetInt32();
        var acknowledgedAtText = root
            .GetProperty("acknowledgedAtUtc")
            .GetString() ?? throw new InvalidDataException(
                "The acknowledgement timestamp is missing.");
        var acknowledgedAt = DateTimeOffset.Parse(
            acknowledgedAtText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        return new RecordingRightsAcknowledgement(
            noticeVersion,
            acknowledgedAt);
    }

    private void BackupInvalidDocument()
    {
        var directory = Path.GetDirectoryName(_path) ??
                        throw new InvalidOperationException(
                            "The acknowledgement path has no parent directory.");
        var name = Path.GetFileNameWithoutExtension(_path);
        var extension = Path.GetExtension(_path);
        var timestamp = _clock.LocalNow
            .ToUniversalTime()
            .ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        for (var ordinal = 1; ; ordinal++)
        {
            var suffix = ordinal == 1
                ? string.Empty
                : $"_{ordinal:000}";
            var backupPath = Path.Combine(
                directory,
                $"{name}.corrupt-{timestamp}{suffix}{extension}");
            try
            {
                File.Move(_path, backupPath, overwrite: false);
                return;
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                // Preserve earlier evidence and try the next ordinal.
            }
        }
    }

    private static void EnsureRegularFileOrMissing(string path)
    {
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The recording-rights document cannot be a reparse point.");
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The recording-rights directory cannot be a reparse point.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

    private sealed record AcknowledgementDocument(
        int SchemaVersion,
        int NoticeVersion,
        DateTimeOffset AcknowledgedAtUtc);
}
