using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VRRecorder.Application.Diagnostics;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Storage;

public sealed class PrivacySafeDiagnosticBundleExporter
    : IDiagnosticBundleExporter
{
    private const int MaximumDiagnosticLineCharacters = 64 * 1024;
    private const string ActiveLogFileName = "vr-recorder.jsonl";
    private const string ReadmeText =
        "VR-Recorder privacy-safe diagnostic bundle\n" +
        "\n" +
        "This bundle is generated only after an explicit user action.\n" +
        "It contains sanitized structured diagnostic events only.\n" +
        "Video, audio, credentials, file paths, user names, world names, " +
        "and OSC avatar values are not included.\n";
    private static readonly DateTimeOffset ArchiveTimestamp = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);
    private static readonly HashSet<string> Levels =
    [
        "error",
        "information",
        "warning",
    ];
    private static readonly HashSet<string> RecorderStates =
    [
        "arming",
        "booting",
        "compliance_fault",
        "countdown",
        "faulted",
        "no_signal",
        "ready",
        "recording",
        "signal_lost",
        "starting",
        "stopping",
    ];
    private static readonly HashSet<string> StorageStates =
    [
        "healthy",
        "stop_required",
        "warning",
    ];
    private static readonly HashSet<string> CameraWarningReasons =
    [
        "insufficient_storage",
        "no_signal",
        "recording_completed",
        "stale_lease_recovery",
        "start_canceled",
        "start_failed",
    ];
    private static readonly HashSet<string> SafeFailureTypes =
    [
        "AggregateException",
        "Exception",
        "HttpRequestException",
        "InvalidDataException",
        "InvalidOperationException",
        "IOException",
        "ObjectDisposedException",
        "OperationCanceledException",
        "SocketException",
        "TimeoutException",
        "UnauthorizedAccessException",
    ];
    private static readonly HashSet<string> AudioInputs =
    [
        "desktop",
        "microphone",
    ];
    private static readonly HashSet<string> AudioWarningKinds =
    [
        "endpoint_rediscovery_failed",
        "input_unavailable",
    ];
    private static readonly HashSet<string> AudioStatusKinds =
    [
        "endpoint_rediscovery_scheduled",
        "input_recovered",
    ];
    private static readonly HashSet<string> Encoders =
    [
        "amf",
        "media_foundation_software",
        "nvenc",
        "qsv",
    ];
    private static readonly HashSet<string> GpuVendors =
    [
        "amd",
        "intel",
        "nvidia",
        "unknown",
    ];
    private static readonly HashSet<string> SourcePixelFormats =
    [
        "bgra8",
        "nv12",
        "rgba8",
    ];
    private readonly string _logDirectory;

    public PrivacySafeDiagnosticBundleExporter(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        if (!Path.IsPathFullyQualified(logDirectory))
        {
            throw new ArgumentException(
                "The diagnostic log directory must be absolute.",
                nameof(logDirectory));
        }

        _logDirectory = Path.GetFullPath(logDirectory);
    }

    public async Task<DiagnosticBundleExport> ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException(
                "The diagnostic bundle destination must be absolute.",
                nameof(destinationPath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(destination) ??
                                   throw new InvalidOperationException(
                                       "The diagnostic bundle destination has no parent directory.");
        EnsureRegularDirectory(destinationDirectory);
        var logPaths = DiscoverLogPaths();
        EnsureRegularFileOrMissing(destination);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        var moved = false;
        try
        {
            int eventCount;
            await using (var bundleStream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous |
                             FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(
                           bundleStream,
                           ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    await WriteReadmeAsync(archive, cancellationToken)
                        .ConfigureAwait(false);
                    eventCount = await WriteDiagnosticsAsync(
                            archive,
                            logPaths,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await bundleStream
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                bundleStream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRegularFileOrMissing(destination);
            File.Move(temporaryPath, destination, overwrite: true);
            moved = true;
            return new DiagnosticBundleExport(destination, eventCount);
        }
        finally
        {
            if (!moved && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private List<string> DiscoverLogPaths()
    {
        if (!Directory.Exists(_logDirectory))
        {
            return [];
        }

        EnsureRegularDirectory(_logDirectory);
        var paths = new List<string>(
            RotatingJsonLinesDiagnosticLog.DefaultMaximumFileCount);
        for (var ordinal =
                 RotatingJsonLinesDiagnosticLog.DefaultMaximumFileCount - 1;
             ordinal >= 1;
             ordinal--)
        {
            AddLogIfPresent(
                paths,
                Path.Combine(
                    _logDirectory,
                    $"vr-recorder.{ordinal.ToString(CultureInfo.InvariantCulture)}.jsonl"));
        }

        AddLogIfPresent(paths, Path.Combine(_logDirectory, ActiveLogFileName));
        return paths;
    }

    private static void AddLogIfPresent(
        List<string> paths,
        string path)
    {
        if (!PathExists(path))
        {
            return;
        }

        EnsureRegularFile(path);
        if (new FileInfo(path).Length >
            RotatingJsonLinesDiagnosticLog.DefaultMaximumFileBytes)
        {
            throw new InvalidDataException(
                "A diagnostic log exceeds the retention file-size limit.");
        }

        paths.Add(path);
    }

    private static async Task WriteReadmeAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = CreateEntry(archive, "README.txt");
        await using var stream = entry.Open();
        await using var writer = NewWriter(stream);
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(ReadmeText.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> WriteDiagnosticsAsync(
        ZipArchive archive,
        IReadOnlyList<string> logPaths,
        CancellationToken cancellationToken)
    {
        var entry = CreateEntry(archive, "diagnostics.jsonl");
        await using var output = entry.Open();
        await using var writer = NewWriter(output);
        var eventCount = 0;
        foreach (var path in logPaths)
        {
            EnsureRegularFile(path);
            await using var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length >
                RotatingJsonLinesDiagnosticLog.DefaultMaximumFileBytes)
            {
                throw new InvalidDataException(
                    "A diagnostic log exceeds the retention file-size limit.");
            }

            using var reader = new StreamReader(
                input,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 64 * 1024,
                leaveOpen: true);
            while (await reader
                       .ReadLineAsync(cancellationToken)
                       .ConfigureAwait(false) is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sanitized = TrySanitize(line);
                if (sanitized is null)
                {
                    continue;
                }

                await writer
                    .WriteLineAsync(sanitized.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                eventCount = checked(eventCount + 1);
            }
        }

        return eventCount;
    }

    private static string? TrySanitize(string line)
    {
        if (line.Length is 0 or > MaximumDiagnosticLineCharacters)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(
                line,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
            var root = UniqueProperties(document.RootElement);
            if (root is null ||
                !TryReadTimestamp(root, out var timestamp) ||
                !TryReadAllowedString(root, "level", Levels, out var level) ||
                !TryReadString(root, "event", out var eventName) ||
                !root.TryGetValue("fields", out var fieldsElement))
            {
                return null;
            }

            var fields = UniqueProperties(fieldsElement);
            if (fields is null)
            {
                return null;
            }

            var sanitizedFields = SanitizeFields(eventName, fields);
            if (sanitizedFields is null)
            {
                return null;
            }

            return JsonSerializer.Serialize(new
            {
                timestampUtc = timestamp.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                level,
                @event = eventName,
                fields = sanitizedFields,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SortedDictionary<string, string>? SanitizeFields(
        string eventName,
        IReadOnlyDictionary<string, JsonElement> fields) => eventName switch
        {
            "recording.state_transition" => SanitizeStateTransition(fields),
            "recording.storage" => SanitizeStorage(fields),
            "recording.saved" => SanitizeSaved(fields),
            "camera.restore_warning" => SanitizeCameraWarning(fields),
            "audio.input_warning" => SanitizeAudioWarning(fields),
            "audio.input_status" => SanitizeAudioStatus(fields),
            "recording.media_profile" => SanitizeMediaProfile(fields),
            "recording.media_statistics" => SanitizeMediaStatistics(fields),
            _ => null,
        };

    private static SortedDictionary<string, string>? SanitizeStateTransition(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadString(fields, "revision", out var revisionText) ||
            !long.TryParse(
                revisionText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision) ||
            revision < 0 ||
            !TryReadAllowedString(
                fields,
                "state",
                RecorderStates,
                out var state))
        {
            return null;
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["revision"] = revision.ToString(CultureInfo.InvariantCulture),
            ["state"] = state,
        };
    }

    private static SortedDictionary<string, string>? SanitizeStorage(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadString(fields, "availableBytes", out var bytesText) ||
            !long.TryParse(
                bytesText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var availableBytes) ||
            availableBytes < 0 ||
            !TryReadString(
                fields,
                "estimatedRemainingSeconds",
                out var secondsText) ||
            !decimal.TryParse(
                secondsText,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var remainingSeconds) ||
            remainingSeconds < 0 ||
            !TryReadAllowedString(
                fields,
                "state",
                StorageStates,
                out var state))
        {
            return null;
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["availableBytes"] = availableBytes.ToString(
                CultureInfo.InvariantCulture),
            ["estimatedRemainingSeconds"] = remainingSeconds.ToString(
                "0.###",
                CultureInfo.InvariantCulture),
            ["state"] = state,
        };
    }

    private static SortedDictionary<string, string>? SanitizeSaved(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadString(fields, "container", out var container) ||
            container != "mp4" ||
            !TryReadString(fields, "result", out var result) ||
            result != "saved")
        {
            return null;
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["container"] = "mp4",
            ["result"] = "saved",
        };
    }

    private static SortedDictionary<string, string>? SanitizeCameraWarning(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadAllowedString(
                fields,
                "reason",
                CameraWarningReasons,
                out var reason))
        {
            return null;
        }

        var sanitized = new SortedDictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["reason"] = reason,
        };
        if (TryReadAllowedString(
                fields,
                "failureType",
                SafeFailureTypes,
                out var failureType))
        {
            sanitized["failureType"] = failureType;
        }

        return sanitized;
    }

    private static SortedDictionary<string, string>? SanitizeAudioWarning(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadNonnegativeLong(
                fields,
                "framePosition",
                out var framePosition) ||
            !TryReadAllowedString(
                fields,
                "input",
                AudioInputs,
                out var input) ||
            !TryReadAllowedString(
                fields,
                "kind",
                AudioWarningKinds,
                out var kind))
        {
            return null;
        }

        var sanitized = new SortedDictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["framePosition"] = framePosition.ToString(
                CultureInfo.InvariantCulture),
            ["input"] = input,
            ["kind"] = kind,
        };
        if (TryReadAllowedString(
                fields,
                "failureType",
                SafeFailureTypes,
                out var failureType))
        {
            sanitized["failureType"] = failureType;
        }

        return sanitized;
    }

    private static SortedDictionary<string, string>? SanitizeAudioStatus(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadNonnegativeLong(
                fields,
                "framePosition",
                out var framePosition) ||
            !TryReadAllowedString(
                fields,
                "input",
                AudioInputs,
                out var input) ||
            !TryReadAllowedString(
                fields,
                "kind",
                AudioStatusKinds,
                out var kind))
        {
            return null;
        }

        var sanitized = new SortedDictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["framePosition"] = framePosition.ToString(
                CultureInfo.InvariantCulture),
            ["input"] = input,
            ["kind"] = kind,
        };
        if (fields.ContainsKey("rediscoveryBudgetMilliseconds"))
        {
            if (!TryReadString(
                    fields,
                    "rediscoveryBudgetMilliseconds",
                    out var budgetText) ||
                !decimal.TryParse(
                    budgetText,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var budget) ||
                budget < 0)
            {
                return null;
            }

            sanitized["rediscoveryBudgetMilliseconds"] = budget.ToString(
                "0.###",
                CultureInfo.InvariantCulture);
        }

        return sanitized;
    }

    private static SortedDictionary<string, string>? SanitizeMediaProfile(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadAllowedString(
                fields,
                "encoder",
                Encoders,
                out var encoder) ||
            !TryReadPositiveDecimal(
                fields,
                "estimatedSourceFramesPerSecond",
                maximum: 1_000,
                out var estimatedSourceFramesPerSecond) ||
            !TryReadAllowedString(
                fields,
                "gpuVendor",
                GpuVendors,
                out var gpuVendor) ||
            !TryReadPositiveInt(
                fields,
                "outputFramesPerSecond",
                out var outputFramesPerSecond) ||
            outputFramesPerSecond > 1_000 ||
            !TryReadPositiveInt(fields, "outputHeight", out var outputHeight) ||
            !TryReadPositiveInt(fields, "outputWidth", out var outputWidth) ||
            !TryReadPositiveInt(fields, "sourceHeight", out var sourceHeight) ||
            !TryReadAllowedString(
                fields,
                "sourcePixelFormat",
                SourcePixelFormats,
                out var sourcePixelFormat) ||
            !TryReadPositiveInt(fields, "sourceWidth", out var sourceWidth))
        {
            return null;
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["encoder"] = encoder,
            ["estimatedSourceFramesPerSecond"] =
                estimatedSourceFramesPerSecond.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture),
            ["gpuVendor"] = gpuVendor,
            ["outputFramesPerSecond"] = outputFramesPerSecond.ToString(
                CultureInfo.InvariantCulture),
            ["outputHeight"] = outputHeight.ToString(
                CultureInfo.InvariantCulture),
            ["outputWidth"] = outputWidth.ToString(
                CultureInfo.InvariantCulture),
            ["sourceHeight"] = sourceHeight.ToString(
                CultureInfo.InvariantCulture),
            ["sourcePixelFormat"] = sourcePixelFormat,
            ["sourceWidth"] = sourceWidth.ToString(
                CultureInfo.InvariantCulture),
        };
    }

    private static SortedDictionary<string, string>? SanitizeMediaStatistics(
        IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (!TryReadLong(
                fields,
                "audioVideoOffsetMicroseconds",
                out var audioVideoOffset) ||
            !TryReadNonnegativeUlong(
                fields,
                "droppedSourceVideoFrameCount",
                out var droppedSourceVideoFrameCount) ||
            !TryReadNonnegativeUlong(
                fields,
                "duplicatedOutputVideoFrameCount",
                out var duplicatedOutputVideoFrameCount) ||
            !TryReadNonnegativeUlong(
                fields,
                "latestEncodeLatencyMicroseconds",
                out var latestEncodeLatency) ||
            !TryReadNonnegativeUlong(
                fields,
                "maximumEncodeLatencyMicroseconds",
                out var maximumEncodeLatency) ||
            maximumEncodeLatency < latestEncodeLatency ||
            !TryReadNonnegativeUlong(
                fields,
                "muxedAudioPacketCount",
                out var muxedAudioPacketCount) ||
            !TryReadNonnegativeUlong(
                fields,
                "muxedVideoPacketCount",
                out var muxedVideoPacketCount) ||
            !TryReadNonnegativeUlong(
                fields,
                "sourceVideoFrameCount",
                out var sourceVideoFrameCount))
        {
            return null;
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["audioVideoOffsetMicroseconds"] = audioVideoOffset.ToString(
                CultureInfo.InvariantCulture),
            ["droppedSourceVideoFrameCount"] =
                droppedSourceVideoFrameCount.ToString(
                    CultureInfo.InvariantCulture),
            ["duplicatedOutputVideoFrameCount"] =
                duplicatedOutputVideoFrameCount.ToString(
                    CultureInfo.InvariantCulture),
            ["latestEncodeLatencyMicroseconds"] = latestEncodeLatency.ToString(
                CultureInfo.InvariantCulture),
            ["maximumEncodeLatencyMicroseconds"] =
                maximumEncodeLatency.ToString(CultureInfo.InvariantCulture),
            ["muxedAudioPacketCount"] = muxedAudioPacketCount.ToString(
                CultureInfo.InvariantCulture),
            ["muxedVideoPacketCount"] = muxedVideoPacketCount.ToString(
                CultureInfo.InvariantCulture),
            ["sourceVideoFrameCount"] = sourceVideoFrameCount.ToString(
                CultureInfo.InvariantCulture),
        };
    }

    private static Dictionary<string, JsonElement>? UniqueProperties(
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                return null;
            }
        }

        return properties;
    }

    private static bool TryReadTimestamp(
        IReadOnlyDictionary<string, JsonElement> properties,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        return TryReadString(properties, "timestampUtc", out var text) &&
               DateTimeOffset.TryParseExact(
                   text,
                   "O",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out timestamp) &&
               timestamp.Offset == TimeSpan.Zero;
    }

    private static bool TryReadAllowedString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        HashSet<string> allowed,
        out string value) =>
        TryReadString(properties, name, out value) &&
        allowed.Contains(value);

    private static bool TryReadNonnegativeLong(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        out long value)
    {
        value = 0;
        return TryReadString(properties, name, out var text) &&
               long.TryParse(
                   text,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value >= 0;
    }

    private static bool TryReadLong(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        out long value)
    {
        value = 0;
        return TryReadString(properties, name, out var text) &&
               long.TryParse(
                   text,
                   NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static bool TryReadNonnegativeUlong(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        out ulong value)
    {
        value = 0;
        return TryReadString(properties, name, out var text) &&
               ulong.TryParse(
                   text,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static bool TryReadPositiveInt(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        out int value)
    {
        value = 0;
        return TryReadString(properties, name, out var text) &&
               int.TryParse(
                   text,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value > 0;
    }

    private static bool TryReadPositiveDecimal(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        decimal maximum,
        out decimal value)
    {
        value = 0;
        return TryReadString(properties, name, out var text) &&
               decimal.TryParse(
                   text,
                   NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value > 0 &&
               value <= maximum;
    }

    private static bool TryReadString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(name, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static ZipArchiveEntry CreateEntry(
        ZipArchive archive,
        string name)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = ArchiveTimestamp;
        return entry;
    }

    private static StreamWriter NewWriter(Stream stream) => new(
        stream,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        bufferSize: 4096,
        leaveOpen: false)
    {
        NewLine = "\n",
    };

    private static void EnsureRegularDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"The diagnostic directory does not exist: {path}");
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "A diagnostic directory cannot be a reparse point.");
        }
    }

    private static void EnsureRegularFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException(
                "A diagnostic log must be a regular file.");
        }
    }

    private static void EnsureRegularFileOrMissing(string path)
    {
        if (!PathExists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException(
                "A diagnostic bundle must be a regular file.");
        }
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
