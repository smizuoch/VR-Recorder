using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Distribution;

internal enum HardwareValidationCaseStatus
{
    Pass,
    Fail,
    Skip,
}

internal enum HardwareValidationArtifactKind
{
    Diagnostic,
    Media,
    Screenshot,
    Oracle,
    Log,
}

internal enum HardwareEncoderMode
{
    Hardware,
    Software,
}

internal enum HardwareEncoderApi
{
    Nvenc,
    Amf,
    Qsv,
    MediaFoundation,
}

internal sealed record HardwareValidationOperatingSystem(
    string Profile,
    string Build,
    string Architecture);

internal sealed record HardwareValidationGpu(
    string Vendor,
    string DeviceId,
    string DriverVersion);

internal sealed record HardwareValidationEncoder(
    HardwareEncoderMode Mode,
    HardwareEncoderApi Api,
    string Name);

internal sealed record HardwareValidationSteamVr(
    string RuntimeVersion,
    string HmdModel,
    string LeftController,
    string RightController)
{
    public bool IsConnected => RuntimeVersion != "not-connected";
}

internal sealed record HardwareValidationEnvironment(
    HardwareValidationOperatingSystem OperatingSystem,
    HardwareValidationGpu Gpu,
    HardwareValidationEncoder Encoder,
    HardwareValidationSteamVr SteamVr);

internal sealed record HardwareValidationArtifact(
    string Path,
    long Length,
    string Sha256,
    HardwareValidationArtifactKind Kind);

internal sealed record HardwareValidationCase(
    string Id,
    HardwareValidationCaseStatus Status,
    IReadOnlyList<HardwareValidationArtifact> Artifacts);

internal sealed record HardwareValidationRun(
    Guid RunId,
    string RunnerId,
    DateTimeOffset CapturedAtUtc,
    HardwareValidationEnvironment Environment,
    IReadOnlyList<HardwareValidationCase> Cases);

internal sealed record WindowsHardwareValidationReport(
    int SchemaVersion,
    string MatrixProfile,
    string PayloadIdentitySha256,
    IReadOnlyList<HardwareValidationRun> Runs,
    string ReportSha256);

internal static class WindowsHardwareValidationReportReader
{
    private const int SchemaVersion = 1;
    private const string MatrixProfile =
        "full-production-hardware-validation-v1";
    private const int MaximumReportBytes = 32 * 1024 * 1024;
    private const int MaximumRuns = 128;
    private const int MaximumCasesPerRun = 256;
    private const int MaximumArtifactsPerCase = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "matrixProfile",
        "payloadIdentitySha256",
        "runs",
    ];
    private static readonly string[] RunProperties =
    [
        "runId",
        "runnerId",
        "capturedAtUtc",
        "environment",
        "cases",
    ];
    private static readonly string[] EnvironmentProperties =
    [
        "os",
        "gpu",
        "encoder",
        "steamVr",
    ];
    private static readonly string[] OperatingSystemProperties =
    [
        "profile",
        "build",
        "architecture",
    ];
    private static readonly string[] GpuProperties =
    [
        "vendor",
        "deviceId",
        "driverVersion",
    ];
    private static readonly string[] EncoderProperties =
    [
        "mode",
        "api",
        "name",
    ];
    private static readonly string[] SteamVrProperties =
    [
        "runtimeVersion",
        "hmdModel",
        "leftController",
        "rightController",
    ];
    private static readonly string[] CaseProperties =
    [
        "id",
        "status",
        "artifacts",
    ];
    private static readonly string[] ArtifactProperties =
    [
        "path",
        "length",
        "sha256",
        "kind",
    ];

    public static WindowsHardwareValidationReport Read(byte[] utf8Content)
    {
        ArgumentNullException.ThrowIfNull(utf8Content);
        if (utf8Content.Length is <= 0 or > MaximumReportBytes)
        {
            throw Invalid();
        }

        try
        {
            using var document = JsonDocument.Parse(
                StrictUtf8.GetString(utf8Content),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 12,
                });
            var root = document.RootElement;
            RequireExactProperties(root, RootProperties);
            if (RequiredInt32(root, "schemaVersion") != SchemaVersion ||
                RequiredString(root, "matrixProfile") != MatrixProfile)
            {
                throw Invalid();
            }

            var payloadIdentitySha256 = RequiredString(
                root,
                "payloadIdentitySha256");
            if (!IsSha256(payloadIdentitySha256))
            {
                throw Invalid();
            }

            var runsElement = root.GetProperty("runs");
            if (runsElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid();
            }

            var runs = runsElement.EnumerateArray()
                .Select(ParseRun)
                .ToArray();
            if (runs.Length is <= 0 or > MaximumRuns ||
                runs.Select(run => run.RunId).Distinct().Count() != runs.Length)
            {
                throw Invalid();
            }

            var artifactPaths = runs
                .SelectMany(run => run.Cases)
                .SelectMany(testCase => testCase.Artifacts)
                .Select(artifact => artifact.Path)
                .ToArray();
            if (artifactPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                artifactPaths.Length)
            {
                throw Invalid();
            }

            return new WindowsHardwareValidationReport(
                SchemaVersion,
                MatrixProfile,
                payloadIdentitySha256,
                runs,
                Convert.ToHexString(SHA256.HashData(utf8Content))
                    .ToLowerInvariant());
        }
        catch (Exception exception) when (exception is
            JsonException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or
            ArgumentException or FormatException)
        {
            throw new InvalidDataException(
                "The Windows hardware validation report is invalid.",
                exception);
        }
    }

    private static HardwareValidationRun ParseRun(JsonElement element)
    {
        RequireExactProperties(element, RunProperties);
        var runIdText = RequiredString(element, "runId");
        if (!Guid.TryParseExact(runIdText, "D", out var runId) ||
            runIdText != runId.ToString("D"))
        {
            throw Invalid();
        }

        var runnerId = RequiredString(element, "runnerId");
        if (!IsCanonicalToken(runnerId, 128))
        {
            throw Invalid();
        }

        var capturedText = RequiredString(element, "capturedAtUtc");
        if (!DateTimeOffset.TryParseExact(
                capturedText,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var capturedAtUtc) ||
            capturedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid();
        }

        var environment = ParseEnvironment(element.GetProperty("environment"));
        var casesElement = element.GetProperty("cases");
        if (casesElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }

        var cases = casesElement.EnumerateArray()
            .Select(ParseCase)
            .ToArray();
        if (cases.Length is <= 0 or > MaximumCasesPerRun ||
            cases.Select(testCase => testCase.Id)
                .Distinct(StringComparer.Ordinal).Count() != cases.Length)
        {
            throw Invalid();
        }

        return new HardwareValidationRun(
            runId,
            runnerId,
            capturedAtUtc,
            environment,
            cases);
    }

    private static HardwareValidationEnvironment ParseEnvironment(
        JsonElement element)
    {
        RequireExactProperties(element, EnvironmentProperties);
        return new HardwareValidationEnvironment(
            ParseOperatingSystem(element.GetProperty("os")),
            ParseGpu(element.GetProperty("gpu")),
            ParseEncoder(element.GetProperty("encoder")),
            ParseSteamVr(element.GetProperty("steamVr")));
    }

    private static HardwareValidationOperatingSystem ParseOperatingSystem(
        JsonElement element)
    {
        RequireExactProperties(element, OperatingSystemProperties);
        var profile = RequiredString(element, "profile");
        var build = RequiredString(element, "build");
        var architecture = RequiredString(element, "architecture");
        if (profile is not ("windows-10-22h2" or "windows-11") ||
            !IsVersionText(build) || architecture != "x64")
        {
            throw Invalid();
        }

        return new HardwareValidationOperatingSystem(
            profile,
            build,
            architecture);
    }

    private static HardwareValidationGpu ParseGpu(JsonElement element)
    {
        RequireExactProperties(element, GpuProperties);
        var vendor = RequiredString(element, "vendor");
        var deviceId = RequiredString(element, "deviceId");
        var driverVersion = RequiredString(element, "driverVersion");
        if (vendor is not ("nvidia" or "amd" or "intel") ||
            !IsTechnicalIdentifier(deviceId, 128) ||
            !IsVersionText(driverVersion))
        {
            throw Invalid();
        }

        return new HardwareValidationGpu(vendor, deviceId, driverVersion);
    }

    private static HardwareValidationEncoder ParseEncoder(JsonElement element)
    {
        RequireExactProperties(element, EncoderProperties);
        var mode = RequiredString(element, "mode") switch
        {
            "hardware" => HardwareEncoderMode.Hardware,
            "software" => HardwareEncoderMode.Software,
            _ => throw Invalid(),
        };
        var api = RequiredString(element, "api") switch
        {
            "nvenc" => HardwareEncoderApi.Nvenc,
            "amf" => HardwareEncoderApi.Amf,
            "qsv" => HardwareEncoderApi.Qsv,
            "media-foundation" => HardwareEncoderApi.MediaFoundation,
            _ => throw Invalid(),
        };
        var name = RequiredString(element, "name");
        if (!IsSafeDisplayText(name, 256) ||
            mode == HardwareEncoderMode.Software &&
            api != HardwareEncoderApi.MediaFoundation ||
            mode == HardwareEncoderMode.Hardware &&
            api == HardwareEncoderApi.MediaFoundation)
        {
            throw Invalid();
        }

        return new HardwareValidationEncoder(mode, api, name);
    }

    private static HardwareValidationSteamVr ParseSteamVr(JsonElement element)
    {
        RequireExactProperties(element, SteamVrProperties);
        var runtimeVersion = RequiredString(element, "runtimeVersion");
        var hmdModel = RequiredString(element, "hmdModel");
        var leftController = RequiredString(element, "leftController");
        var rightController = RequiredString(element, "rightController");
        var disconnected = runtimeVersion == "not-connected";
        if (disconnected != (hmdModel == "not-connected") ||
            disconnected != (leftController == "not-connected") ||
            disconnected != (rightController == "not-connected") ||
            !IsSafeDisplayText(runtimeVersion, 128) ||
            !IsSafeDisplayText(hmdModel, 256) ||
            !IsSafeDisplayText(leftController, 256) ||
            !IsSafeDisplayText(rightController, 256))
        {
            throw Invalid();
        }

        return new HardwareValidationSteamVr(
            runtimeVersion,
            hmdModel,
            leftController,
            rightController);
    }

    private static HardwareValidationCase ParseCase(JsonElement element)
    {
        RequireExactProperties(element, CaseProperties);
        var id = RequiredString(element, "id");
        if (!IsCanonicalToken(id, 128))
        {
            throw Invalid();
        }

        var status = RequiredString(element, "status") switch
        {
            "pass" => HardwareValidationCaseStatus.Pass,
            "fail" => HardwareValidationCaseStatus.Fail,
            "skip" => HardwareValidationCaseStatus.Skip,
            _ => throw Invalid(),
        };
        var artifactsElement = element.GetProperty("artifacts");
        if (artifactsElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }

        var artifacts = artifactsElement.EnumerateArray()
            .Select(ParseArtifact)
            .ToArray();
        if (artifacts.Length is <= 0 or > MaximumArtifactsPerCase ||
            artifacts.Select(artifact => artifact.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            artifacts.Length)
        {
            throw Invalid();
        }

        return new HardwareValidationCase(id, status, artifacts);
    }

    private static HardwareValidationArtifact ParseArtifact(
        JsonElement element)
    {
        RequireExactProperties(element, ArtifactProperties);
        var path = WindowsRuntimeRelativePath.RequireCanonical(
            RequiredString(element, "path"),
            "path");
        var length = RequiredInt64(element, "length");
        var sha256 = RequiredString(element, "sha256");
        var kind = RequiredString(element, "kind") switch
        {
            "diagnostic" => HardwareValidationArtifactKind.Diagnostic,
            "media" => HardwareValidationArtifactKind.Media,
            "screenshot" => HardwareValidationArtifactKind.Screenshot,
            "oracle" => HardwareValidationArtifactKind.Oracle,
            "log" => HardwareValidationArtifactKind.Log,
            _ => throw Invalid(),
        };
        if (length < 0 || !IsSha256(sha256))
        {
            throw Invalid();
        }

        return new HardwareValidationArtifact(path, length, sha256, kind);
    }

    private static void RequireExactProperties(
        JsonElement element,
        string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw Invalid();
            }
        }

        if (actual.Count != required.Length ||
            required.Any(property => !actual.Contains(property)))
        {
            throw Invalid();
        }
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw Invalid();
        }

        return property.GetString() is { Length: > 0 } value &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : throw Invalid();
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : throw Invalid();
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out var value)
            ? value
            : throw Invalid();
    }

    private static bool IsCanonicalToken(string value, int maximumLength) =>
        value.Length <= maximumLength &&
        value[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        value.All(character => character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or '.' or '_' or '-');

    private static bool IsTechnicalIdentifier(
        string value,
        int maximumLength) => value.Length <= maximumLength &&
        value.All(character => character is >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or >= '0' and <= '9' or
            '.' or '_' or '-' or ':' or '&');

    private static bool IsVersionText(string value) =>
        value.Length <= 128 && value[0] is >= '0' and <= '9' &&
        value.All(character => character is >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or >= '0' and <= '9' or
            '.' or '_' or '-' or '+');

    private static bool IsSafeDisplayText(string value, int maximumLength) =>
        value.Length <= maximumLength &&
        value.All(character => character is >= ' ' and <= '~');

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InvalidDataException Invalid() => new(
        "The Windows hardware validation report is invalid.");
}
