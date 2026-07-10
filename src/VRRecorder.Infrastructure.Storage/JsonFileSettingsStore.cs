using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;

namespace VRRecorder.Infrastructure.Storage;

public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
    private readonly string _settingsPath;
    private readonly IWallClock _clock;

    public JsonFileSettingsStore(string settingsPath)
        : this(settingsPath, SystemWallClock.Instance)
    {
    }

    public JsonFileSettingsStore(
        string settingsPath,
        IWallClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Path.IsPathFullyQualified(settingsPath))
        {
            throw new ArgumentException(
                "The settings path must be absolute.",
                nameof(settingsPath));
        }

        _settingsPath = Path.GetFullPath(settingsPath);
        _clock = clock;
    }

    public async Task<VRRecorderSettings> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return VRRecorderSettings.CreateDefault();
        }

        try
        {
            return await LoadExistingAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverableDocumentError(exception))
        {
            BackupCorruptDocument();
            return VRRecorderSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(
        VRRecorderSettings settings,
        CancellationToken cancellationToken)
    {
        VRRecorderSettingsContract.Validate(settings);
        var directory = Path.GetDirectoryName(_settingsPath) ??
                        throw new InvalidOperationException(
                            "The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_settingsPath}.tmp-{Guid.NewGuid():N}";
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
                await JsonSerializer
                    .SerializeAsync(
                        stream,
                        settings,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private async Task<VRRecorderSettings> LoadExistingAsync(
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            _settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var settings = await JsonSerializer
            .DeserializeAsync<VRRecorderSettings>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidDataException("The settings document is empty.");
        VRRecorderSettingsContract.Validate(settings);
        return settings;
    }

    private void BackupCorruptDocument()
    {
        var directory = Path.GetDirectoryName(_settingsPath) ??
                        throw new InvalidOperationException(
                            "The settings path has no parent directory.");
        var name = Path.GetFileNameWithoutExtension(_settingsPath);
        var extension = Path.GetExtension(_settingsPath);
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
                File.Move(_settingsPath, backupPath, overwrite: false);
                return;
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                // Preserve earlier recovery evidence and try the next ordinal.
            }
        }
    }

    private static bool IsRecoverableDocumentError(Exception exception) =>
        exception is JsonException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException;
}
