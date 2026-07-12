using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Storage;

public sealed class JsonFileVideoSenderSelectionStore
    : IVideoSenderSelectionStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public JsonFileVideoSenderSelectionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "The video sender selection path must be absolute.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async Task<string?> LoadAsync(
        string vrChatServiceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            var document = await LoadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);
            return document.Selections.TryGetValue(
                vrChatServiceId,
                out var senderId)
                ? senderId
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string vrChatServiceId,
        string senderId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            var existing = await LoadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);
            var selections = new SortedDictionary<string, string>(
                existing.Selections,
                StringComparer.Ordinal)
            {
                [vrChatServiceId] = senderId,
            };
            await SaveDocumentAsync(
                    new SelectionDocument(CurrentSchemaVersion, selections),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }
    }

    private async Task<SelectionDocument> LoadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return EmptyDocument();
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer
            .DeserializeAsync<SelectionDocument>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidDataException(
                "The video sender selection document is empty.");
        Validate(document);
        return document;
    }

    private async Task SaveDocumentAsync(
        SelectionDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path) ??
                        throw new InvalidOperationException(
                            "The video sender selection path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous |
                             FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private static SelectionDocument EmptyDocument() =>
        new(
            CurrentSchemaVersion,
            new SortedDictionary<string, string>(StringComparer.Ordinal));

    private static void Validate(SelectionDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion ||
            document.Selections is null ||
            document.Selections.Any(selection =>
                string.IsNullOrWhiteSpace(selection.Key) ||
                string.IsNullOrWhiteSpace(selection.Value)))
        {
            throw new InvalidDataException(
                "The video sender selection document is invalid.");
        }
    }

    private sealed record SelectionDocument(
        int SchemaVersion,
        SortedDictionary<string, string> Selections);
}
