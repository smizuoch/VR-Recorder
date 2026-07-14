using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Staging;

internal static class WindowsRuntimeStagingManifestReader
{
    private const int SchemaVersion = 1;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumEntryCount = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "entries",
    ];
    private static readonly string[] EntryProperties =
    [
        "source",
        "target",
        "role",
        "componentId",
        "platform",
        "deploymentKind",
        "sha256",
    ];

    public static WindowsRuntimeStagingManifest Read(byte[] utf8Content)
    {
        ArgumentNullException.ThrowIfNull(utf8Content);
        if (utf8Content.Length == 0 ||
            utf8Content.Length > MaximumManifestBytes)
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
                    MaxDepth = 8,
                });
            var root = document.RootElement;
            RequireExactProperties(root, RootProperties);
            if (RequiredInt32(root, "schemaVersion") != SchemaVersion)
            {
                throw Invalid();
            }

            var entriesElement = root.GetProperty("entries");
            if (entriesElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid();
            }

            var entries = entriesElement.EnumerateArray()
                .Select(ParseEntry)
                .ToArray();
            if (entries.Length == 0 || entries.Length > MaximumEntryCount)
            {
                throw Invalid();
            }

            RequireUnique(entries.Select(entry => entry.Source));
            RequireUnique(entries.Select(entry => entry.Target));
            RequireNoFileParentConflicts(entries.Select(entry => entry.Source));
            RequireNoFileParentConflicts(entries.Select(entry => entry.Target));
            var manifestHash = Convert
                .ToHexString(SHA256.HashData(utf8Content))
                .ToLowerInvariant();
            return new WindowsRuntimeStagingManifest(
                SchemaVersion,
                manifestHash,
                entries
                    .OrderBy(entry => entry.Target, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (Exception exception) when (
            exception is JsonException or
                DecoderFallbackException or
                InvalidOperationException or
                KeyNotFoundException or
                ArgumentException)
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest is invalid.",
                exception);
        }
    }

    private static WindowsRuntimeStagingEntry ParseEntry(JsonElement element)
    {
        RequireExactProperties(element, EntryProperties);
        var source = WindowsRuntimeRelativePath.RequireCanonical(
            RequiredString(element, "source"),
            "source");
        var target = WindowsRuntimeRelativePath.RequireCanonical(
            RequiredString(element, "target"),
            "target");
        var role = ParseRole(RequiredString(element, "role"));
        var componentId = RequiredString(element, "componentId");
        if (!IsCanonicalComponentId(componentId))
        {
            throw Invalid();
        }

        var platform = RequiredString(element, "platform");
        if (!string.Equals(
                platform,
                "windows-x64",
                StringComparison.Ordinal))
        {
            throw Invalid();
        }

        var deploymentKind = ParseDeploymentKind(
            RequiredString(element, "deploymentKind"));
        if (deploymentKind != RequiredDeploymentKind(role))
        {
            throw Invalid();
        }

        var sha256 = RequiredString(element, "sha256");
        if (!IsLowerHexSha256(sha256))
        {
            throw Invalid();
        }

        return new WindowsRuntimeStagingEntry(
            source,
            target,
            role,
            componentId,
            platform,
            deploymentKind,
            sha256);
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

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? throw Invalid() : value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : throw Invalid();
    }

    private static WindowsRuntimeRole ParseRole(string role) => role switch
    {
        "first-party-native" => WindowsRuntimeRole.FirstPartyNative,
        "ffmpeg-runtime" => WindowsRuntimeRole.FfmpegRuntime,
        "diagnostic-tool" => WindowsRuntimeRole.DiagnosticTool,
        "openvr-runtime" => WindowsRuntimeRole.OpenVrRuntime,
        "openvr-manifest" => WindowsRuntimeRole.OpenVrManifest,
        "openvr-binding" => WindowsRuntimeRole.OpenVrBinding,
        "spout-runtime" => WindowsRuntimeRole.SpoutRuntime,
        "encoder-runtime" => WindowsRuntimeRole.EncoderRuntime,
        "factory-selection-evidence" =>
            WindowsRuntimeRole.FactorySelectionEvidence,
        "application-asset" => WindowsRuntimeRole.ApplicationAsset,
        _ => throw Invalid(),
    };

    private static WindowsRuntimeDeploymentKind ParseDeploymentKind(
        string kind) => kind switch
        {
            "native-library" => WindowsRuntimeDeploymentKind.NativeLibrary,
            "executable" => WindowsRuntimeDeploymentKind.Executable,
            "asset" => WindowsRuntimeDeploymentKind.Asset,
            "evidence" => WindowsRuntimeDeploymentKind.Evidence,
            _ => throw Invalid(),
        };

    private static WindowsRuntimeDeploymentKind RequiredDeploymentKind(
        WindowsRuntimeRole role) => role switch
        {
            WindowsRuntimeRole.FirstPartyNative or
                WindowsRuntimeRole.FfmpegRuntime or
                WindowsRuntimeRole.OpenVrRuntime or
                WindowsRuntimeRole.SpoutRuntime or
                WindowsRuntimeRole.EncoderRuntime =>
                WindowsRuntimeDeploymentKind.NativeLibrary,
            WindowsRuntimeRole.DiagnosticTool =>
                WindowsRuntimeDeploymentKind.Executable,
            WindowsRuntimeRole.FactorySelectionEvidence =>
                WindowsRuntimeDeploymentKind.Evidence,
            WindowsRuntimeRole.OpenVrManifest or
                WindowsRuntimeRole.OpenVrBinding or
                WindowsRuntimeRole.ApplicationAsset =>
                WindowsRuntimeDeploymentKind.Asset,
            _ => throw Invalid(),
        };

    private static bool IsCanonicalComponentId(string value) =>
        value.Length is > 0 and <= 128 &&
        value[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        value.All(character =>
            character is >= 'a' and <= 'z' or
                >= '0' and <= '9' or '.' or '_' or '-');

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RequireUnique(IEnumerable<string> paths)
    {
        var values = paths.ToArray();
        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            values.Length)
        {
            throw Invalid();
        }
    }

    private static void RequireNoFileParentConflicts(
        IEnumerable<string> paths)
    {
        var values = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in values)
        {
            var separator = path.IndexOf('/');
            while (separator >= 0)
            {
                if (values.Contains(path[..separator]))
                {
                    throw Invalid();
                }

                separator = path.IndexOf('/', separator + 1);
            }
        }
    }

    private static InvalidDataException Invalid() => new();
}
