using System.Globalization;
using System.Text;
using System.Text.Json;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Infrastructure.Storage;

public sealed class FileSystemCameraLeaseStore : ICameraLeaseStore, IDisposable
{
    private const int SchemaVersion = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly HashSet<string> ExpectedProperties =
    [
        "schemaVersion",
        "sessionId",
        "vrChatServiceId",
        "processId",
        "createdAtUtc",
        "previousModeKnown",
        "previousMode",
        "previousStreamingKnown",
        "previousStreaming",
        "changedModeByRecorder",
        "changedStreamingByRecorder",
    ];

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public FileSystemCameraLeaseStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "The camera lease path must be absolute.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async Task<CameraLease?> LoadAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        CameraLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        EnsurePersistable(lease);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSafeExistingPath();
            var existing = await LoadCoreAsync(cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.HasSamePersistedState(lease))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "A different camera lease is already persisted.");
            }

            var directory = Path.GetDirectoryName(_path) ??
                            throw new InvalidOperationException(
                                "The camera lease path has no parent directory.");
            Directory.CreateDirectory(directory);
            EnsureNoReparsePoint(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                var content = Serialize(lease);
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 FileOptions.Asynchronous |
                                 FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(content, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, _path, overwrite: false);
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

    public async Task DeleteAsync(
        CameraLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        EnsurePersistable(lease);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await LoadCoreAsync(cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return;
            }

            if (!existing.HasSamePersistedState(lease))
            {
                throw new InvalidOperationException(
                    "The persisted camera lease belongs to a different owner.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CameraLease?> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        EnsureSafeExistingPath();
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
                DecoderFallbackException or
                FormatException or
                InvalidOperationException or
                OverflowException)
        {
            throw new InvalidDataException(
                "The persisted camera lease is invalid.",
                exception);
        }
    }

    private void EnsureSafeExistingPath()
    {
        var parent = Path.GetDirectoryName(_path);
        if (parent is not null && Directory.Exists(parent))
        {
            EnsureNoReparsePoint(parent);
        }

        var leaseFile = new FileInfo(_path);
        if (leaseFile.LinkTarget is not null ||
            (leaseFile.Exists &&
             (leaseFile.Attributes & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidDataException(
                "The camera lease file cannot be a reparse point.");
        }
    }

    private static void EnsureNoReparsePoint(string directory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(directory));
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "The camera lease directory cannot traverse a reparse point.");
            }

            current = current.Parent;
        }
    }

    private static byte[] Serialize(CameraLease lease)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("sessionId", lease.SessionId);
            writer.WriteString("vrChatServiceId", lease.VrChatServiceId);
            writer.WriteNumber("processId", lease.ProcessId);
            writer.WriteString(
                "createdAtUtc",
                lease.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteBoolean("previousModeKnown", lease.PreviousMode.IsKnown);
            if (lease.PreviousMode.IsKnown)
            {
                writer.WriteString("previousMode", lease.PreviousMode.Value.ToString());
            }
            else
            {
                writer.WriteNull("previousMode");
            }

            writer.WriteBoolean(
                "previousStreamingKnown",
                lease.PreviousStreaming.IsKnown);
            if (lease.PreviousStreaming.IsKnown)
            {
                writer.WriteBoolean(
                    "previousStreaming",
                    lease.PreviousStreaming.Value);
            }
            else
            {
                writer.WriteNull("previousStreaming");
            }

            writer.WriteBoolean(
                "changedModeByRecorder",
                lease.ChangedModeByRecorder);
            writer.WriteBoolean(
                "changedStreamingByRecorder",
                lease.ChangedStreamingByRecorder);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static CameraLease Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(
            StrictUtf8.GetString(bytes),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4,
            });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            HasDuplicateProperties(root))
        {
            throw new FormatException("The camera lease root is invalid.");
        }

        var names = root.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!names.SetEquals(ExpectedProperties) ||
            RequiredInt32(root, "schemaVersion") != SchemaVersion)
        {
            throw new FormatException("The camera lease schema is invalid.");
        }

        var previousModeKnown = RequiredBoolean(root, "previousModeKnown");
        var previousModeElement = root.GetProperty("previousMode");
        var previousMode = previousModeKnown
            ? ObservedCameraValue.Known(RequiredCameraMode(previousModeElement))
            : previousModeElement.ValueKind == JsonValueKind.Null
                ? ObservedCameraValue.Unknown<CameraMode>()
                : throw new FormatException("Unknown mode must be null.");
        var previousStreamingKnown = RequiredBoolean(
            root,
            "previousStreamingKnown");
        var previousStreamingElement = root.GetProperty("previousStreaming");
        var previousStreaming = previousStreamingKnown
            ? ObservedCameraValue.Known(RequiredBoolean(previousStreamingElement))
            : previousStreamingElement.ValueKind == JsonValueKind.Null
                ? ObservedCameraValue.Unknown<bool>()
                : throw new FormatException("Unknown streaming must be null.");
        var createdAtText = RequiredString(root.GetProperty("createdAtUtc"));
        if (!DateTimeOffset.TryParseExact(
                createdAtText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAtUtc) ||
            createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new FormatException("The camera lease creation time is invalid.");
        }

        return new CameraLease(
            RequiredString(root.GetProperty("sessionId")),
            RequiredString(root.GetProperty("vrChatServiceId")),
            RequiredInt32(root, "processId"),
            createdAtUtc,
            previousMode,
            previousStreaming,
            RequiredBoolean(root, "changedModeByRecorder"),
            RequiredBoolean(root, "changedStreamingByRecorder"));
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) ||
                    HasDuplicateProperties(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasDuplicateProperties(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string RequiredString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!
            : throw new FormatException("A required string is invalid.");

    private static CameraMode RequiredCameraMode(JsonElement element)
    {
        var name = RequiredString(element);
        if (!Enum.TryParse<CameraMode>(name, ignoreCase: false, out var mode) ||
            !Enum.IsDefined(mode) ||
            !string.Equals(Enum.GetName(mode), name, StringComparison.Ordinal))
        {
            throw new FormatException("The previous camera mode is invalid.");
        }

        return mode;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        return element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out var value)
            ? value
            : throw new FormatException($"{name} must be an integer.");
    }

    private static bool RequiredBoolean(JsonElement parent, string name) =>
        RequiredBoolean(parent.GetProperty(name));

    private static bool RequiredBoolean(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException("A required boolean is invalid."),
        };

    private static void EnsurePersistable(CameraLease lease)
    {
        if (!lease.IsPersistable)
        {
            throw new ArgumentException(
                "The camera lease does not have persistent owner identity.",
                nameof(lease));
        }

        if (lease.PreviousMode.IsKnown &&
            !Enum.IsDefined(lease.PreviousMode.Value))
        {
            throw new ArgumentException(
                "The camera lease contains an undefined previous mode.",
                nameof(lease));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
}
