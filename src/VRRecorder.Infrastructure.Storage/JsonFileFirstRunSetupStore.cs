using System.Globalization;
using System.Text.Json;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Infrastructure.Storage;

public sealed class JsonFileFirstRunSetupStore
    : IFirstRunSetupStore, IDisposable
{
    private const int SchemaVersion = 1;
    private static readonly IReadOnlyList<FirstRunSetupStep> RequiredSteps =
        FirstRunSetupController.RequiredSteps;
    private static readonly Dictionary<string, FirstRunSetupStep>
        StepsByName = new Dictionary<string, FirstRunSetupStep>(
            StringComparer.Ordinal)
        {
            ["steamVrDetection"] = FirstRunSetupStep.SteamVrDetection,
            ["vrChatOscDetection"] = FirstRunSetupStep.VrChatOscDetection,
            ["cameraOscEndpoint"] = FirstRunSetupStep.CameraOscEndpoint,
            ["microphonePrivacyAndDevice"] =
                FirstRunSetupStep.MicrophonePrivacyAndDevice,
            ["encoderSelfTest"] = FirstRunSetupStep.EncoderSelfTest,
            ["steamVrActionBinding"] = FirstRunSetupStep.SteamVrActionBinding,
            ["wristOverlayPlacement"] =
                FirstRunSetupStep.WristOverlayPlacement,
            ["testRecordingPlayback"] =
                FirstRunSetupStep.TestRecordingPlayback,
            ["legalBundleVerification"] =
                FirstRunSetupStep.LegalBundleVerification,
            ["offlineLegalAccess"] = FirstRunSetupStep.OfflineLegalAccess,
            ["localizationAccessibility"] =
                FirstRunSetupStep.LocalizationAccessibility,
            ["designAssetConformance"] =
                FirstRunSetupStep.DesignAssetConformance,
        };
    private static readonly Dictionary<FirstRunSetupStep, string>
        NamesByStep = StepsByName.ToDictionary(pair => pair.Value, pair => pair.Key);
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public JsonFileFirstRunSetupStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "The first-run setup progress path must be absolute.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async Task<FirstRunSetupProgress?> LoadAsync(
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
                exception is JsonException or InvalidDataException or
                    ArgumentException or FormatException or OverflowException)
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
        FirstRunSetupProgress progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ValidateOrderedPrefix(progress.CompletedSteps);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureRegularFileOrMissing(_path);
            var directory = Path.GetDirectoryName(_path) ??
                            throw new InvalidOperationException(
                                "The setup progress path has no parent directory.");
            Directory.CreateDirectory(directory);
            EnsureNotReparsePoint(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.tmp-{Guid.NewGuid():N}");
            try
            {
                var json = Serialize(progress);
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous |
                                 FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(json, cancellationToken)
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

    private static byte[] Serialize(FirstRunSetupProgress progress)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
               }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteNumber("setupVersion", progress.SetupVersion);
            writer.WriteStartArray("completedSteps");
            foreach (var step in progress.CompletedSteps)
            {
                writer.WriteStringValue(NamesByStep[step]);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return [.. stream.ToArray(), (byte)'\n'];
    }

    private static FirstRunSetupProgress Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        string[] expected = ["schemaVersion", "setupVersion", "completedSteps"];
        if (root.ValueKind != JsonValueKind.Object ||
            !root.EnumerateObject().Select(item => item.Name)
                .ToHashSet(StringComparer.Ordinal).SetEquals(expected) ||
            root.EnumerateObject().Count() != expected.Length)
        {
            throw new InvalidDataException(
                "The setup progress document has an unexpected property set.");
        }

        if (root.GetProperty("schemaVersion").GetInt32() != SchemaVersion)
        {
            throw new InvalidDataException(
                "The setup progress schema is not supported.");
        }

        var completedElement = root.GetProperty("completedSteps");
        if (completedElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Completed steps must be an array.");
        }

        var completed = completedElement.EnumerateArray().Select(element =>
        {
            var name = element.GetString();
            return name is not null && StepsByName.TryGetValue(name, out var step)
                ? step
                : throw new InvalidDataException("The setup step is unknown.");
        }).ToArray();
        ValidateOrderedPrefix(completed);
        return new FirstRunSetupProgress(
            root.GetProperty("setupVersion").GetInt32(),
            completed);
    }

    private static void ValidateOrderedPrefix(
        IReadOnlyList<FirstRunSetupStep> completed)
    {
        if (completed.Count > RequiredSteps.Count)
        {
            throw new InvalidDataException("Too many setup steps were completed.");
        }

        for (var index = 0; index < completed.Count; index++)
        {
            if (completed[index] != RequiredSteps[index])
            {
                throw new InvalidDataException(
                    "Completed setup steps must be an ordered prefix.");
            }
        }
    }

    private void BackupInvalidDocument()
    {
        var directory = Path.GetDirectoryName(_path) ??
                        throw new InvalidOperationException(
                            "The setup progress path has no parent directory.");
        var name = Path.GetFileNameWithoutExtension(_path);
        var extension = Path.GetExtension(_path);
        var timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture);
        for (var ordinal = 1; ; ordinal++)
        {
            var suffix = ordinal == 1 ? string.Empty : $"_{ordinal:000}";
            var backup = Path.Combine(
                directory,
                $"{name}.corrupt-{timestamp}{suffix}{extension}");
            try
            {
                File.Move(_path, backup, overwrite: false);
                return;
            }
            catch (IOException) when (File.Exists(backup))
            {
            }
        }
    }

    private static void EnsureRegularFileOrMissing(string path)
    {
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The setup progress document cannot be a reparse point.");
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The setup progress directory cannot be a reparse point.");
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
}
