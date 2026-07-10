using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;

namespace VRRecorder.Infrastructure.Storage;

public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
    private readonly string _settingsPath;

    public JsonFileSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        if (!Path.IsPathFullyQualified(settingsPath))
        {
            throw new ArgumentException(
                "The settings path must be absolute.",
                nameof(settingsPath));
        }

        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public async Task<VRRecorderSettings> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return VRRecorderSettings.CreateDefault();
        }

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
}
