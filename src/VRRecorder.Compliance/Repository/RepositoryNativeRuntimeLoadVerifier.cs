using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

public static partial class RepositoryNativeRuntimeLoadVerifier
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions =
        CreateSerializerOptions();

    public static IReadOnlyList<ComplianceIssue> Verify(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var issues = new List<ComplianceIssue>();
        var manifest = ReadManifest(root, issues);
        if (manifest is null)
        {
            return issues;
        }

        if (manifest.SchemaVersion != CurrentSchemaVersion ||
            manifest.Entries is null)
        {
            issues.Add(new ComplianceIssue(
                "unsupported-runtime-load-manifest",
                "third-party/runtime-load-manifest.yml"));
            return issues;
        }

        NativeRuntimeLoadManifestEntry[] entries;
        string[] componentIds;
        try
        {
            entries = manifest.Entries;
            componentIds = ReadRegisteredComponentIds(root);
            ValidateEntrySourcePaths(root, entries);
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or ArgumentException or
            NotSupportedException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-runtime-load-manifest",
                "third-party/runtime-load-manifest.yml"));
            return issues;
        }

        var observations = entries.Select(entry =>
            new NativeRuntimeLoadObservation(
                entry.Consumer,
                entry.FileName,
                entry.Mechanism,
                entry.Platform));
        var admissions = entries.Select(entry =>
            new NativeRuntimeLoadAdmission(
                entry.Consumer,
                entry.FileName,
                entry.Mechanism,
                entry.Platform,
                entry.Origin,
                entry.Integrity,
                entry.ComponentId));
        try
        {
            issues.AddRange(NativeRuntimeLoadAdmissionValidator
                .Validate(observations, admissions, componentIds)
                .Issues);
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-runtime-load-manifest",
                "third-party/runtime-load-manifest.yml"));
            return issues;
        }

        VerifyCallSites(root, entries, issues);
        return issues;
    }

    private static NativeRuntimeLoadManifestDocument? ReadManifest(
        string root,
        List<ComplianceIssue> issues)
    {
        var path = Path.Combine(
            root,
            "third-party",
            "runtime-load-manifest.yml");
        if (!File.Exists(path))
        {
            issues.Add(new ComplianceIssue(
                "missing-runtime-load-manifest",
                "third-party/runtime-load-manifest.yml"));
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<NativeRuntimeLoadManifestDocument>(
                       stream,
                       SerializerOptions) ??
                   throw new JsonException("The runtime-load manifest is null.");
        }
        catch (JsonException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-runtime-load-manifest",
                "third-party/runtime-load-manifest.yml"));
            return null;
        }
    }

    private static string[] ReadRegisteredComponentIds(string root)
    {
        var path = Path.Combine(root, "third-party", "registry.yml");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement
            .GetProperty("components")
            .EnumerateArray()
            .Select(component => component.GetProperty("id").GetString() ??
                                 throw new InvalidDataException(
                                     "A registry component ID is null."))
            .ToArray();
    }

    private static void ValidateEntrySourcePaths(
        string root,
        IEnumerable<NativeRuntimeLoadManifestEntry> entries)
    {
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.SourcePaths is null || entry.SourcePaths.Length == 0 ||
                entry.SourcePaths.Distinct(StringComparer.Ordinal).Count() !=
                entry.SourcePaths.Length)
            {
                throw new InvalidDataException(
                    "Every runtime load needs unique source paths.");
            }

            foreach (var sourcePath in entry.SourcePaths)
            {
                if (!TryResolveSourcePath(root, sourcePath, out var fullPath) ||
                    !File.Exists(fullPath))
                {
                    throw new InvalidDataException(
                        "A runtime-load source path is missing or unsafe.");
                }
            }
        }
    }

    private static void VerifyCallSites(
        string root,
        IReadOnlyList<NativeRuntimeLoadManifestEntry> entries,
        List<ComplianceIssue> issues)
    {
        var callSites = DiscoverCallSites(root);
        foreach (var callSite in callSites)
        {
            var matches = entries.Where(entry =>
                    entry.Mechanism == callSite.Mechanism &&
                    entry.SourcePaths.Contains(
                        callSite.SourcePath,
                        StringComparer.Ordinal) &&
                    (callSite.FileName is null || string.Equals(
                        entry.FileName,
                        callSite.FileName,
                        StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matches.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "unregistered-runtime-load",
                    callSite.SourcePath));
            }
            else if (matches.Length != 1)
            {
                issues.Add(new ComplianceIssue(
                    "ambiguous-runtime-load-callsite",
                    callSite.SourcePath));
            }
        }

        foreach (var entry in entries)
        {
            foreach (var sourcePath in entry.SourcePaths)
            {
                if (!callSites.Any(callSite =>
                        callSite.Mechanism == entry.Mechanism &&
                        string.Equals(
                            callSite.SourcePath,
                            sourcePath,
                            StringComparison.Ordinal) &&
                        (callSite.FileName is null || string.Equals(
                            callSite.FileName,
                            entry.FileName,
                            StringComparison.OrdinalIgnoreCase))))
                {
                    issues.Add(new ComplianceIssue(
                        "missing-runtime-load-callsite",
                        sourcePath));
                }
            }
        }
    }

    private static DiscoveredRuntimeLoadCallSite[] DiscoverCallSites(string root)
    {
        var sourceRoot = Path.Combine(root, "src");
        var callSites = new List<DiscoveredRuntimeLoadCallSite>();
        foreach (var path in Directory.EnumerateFiles(
                     sourceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var sourcePath = Path.GetRelativePath(root, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            var code = File.ReadAllText(path);
            foreach (Match match in NativeLibraryLoadRegex().Matches(code))
            {
                callSites.Add(new DiscoveredRuntimeLoadCallSite(
                    sourcePath,
                    NativeRuntimeLoadMechanism.NativeLibrary,
                    FileName: null));
            }

            foreach (Match match in LibraryImportRegex().Matches(code))
            {
                callSites.Add(new DiscoveredRuntimeLoadCallSite(
                    sourcePath,
                    NativeRuntimeLoadMechanism.LibraryImport,
                    match.Groups[1].Value));
            }
        }

        return callSites.ToArray();
    }

    private static bool TryResolveSourcePath(
        string root,
        string relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            !relativePath.StartsWith("src/", StringComparison.Ordinal))
        {
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var sourceRoot = Path.GetFullPath(Path.Combine(root, "src"));
        var prefix = Path.TrimEndingDirectorySeparator(sourceRoot) +
                     Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(prefix, comparison);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [GeneratedRegex(@"\bNativeLibrary\.Load\s*\(")]
    private static partial Regex NativeLibraryLoadRegex();

    [GeneratedRegex(
        "\\[(?:LibraryImport|DllImport)\\s*\\(\\s*\"([^\"\\r\\n]+)\"")]
    private static partial Regex LibraryImportRegex();

    private sealed record NativeRuntimeLoadManifestDocument(
        int SchemaVersion,
        NativeRuntimeLoadManifestEntry[] Entries);

    private sealed record NativeRuntimeLoadManifestEntry(
        string Consumer,
        string FileName,
        NativeRuntimeLoadMechanism Mechanism,
        string Platform,
        NativeDependencyOrigin Origin,
        NativeRuntimeIntegrity Integrity,
        string? ComponentId,
        string[] SourcePaths);

    private sealed record DiscoveredRuntimeLoadCallSite(
        string SourcePath,
        NativeRuntimeLoadMechanism Mechanism,
        string? FileName);
}
