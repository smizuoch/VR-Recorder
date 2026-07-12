using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

public static partial class RepositoryNativeLinkVerifier
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
                "unsupported-native-link-manifest",
                "third-party/native-link-manifest.yml"));
            return issues;
        }

        NativeLinkManifestEntry[] entries;
        DiscoveredNativeLink[] discovered;
        NativeArtifactRegistry registry;
        try
        {
            entries = manifest.Entries;
            ValidateEntries(root, entries);
            discovered = DiscoverNativeLinks(root);
            registry = NativeArtifactRegistryReader.Read(root);
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or ArgumentException or
            NotSupportedException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-native-link-manifest",
                "third-party/native-link-manifest.yml"));
            return issues;
        }

        var observations = discovered.Select(link =>
            new NativeLinkObservation(
                link.ConsumerTarget,
                link.InputIdentity,
                link.InputKind,
                link.Platform));
        var admissions = entries.Select(entry =>
            new NativeDependencyAdmission(
                entry.ConsumerTarget,
                entry.InputIdentity,
                entry.InputKind,
                entry.Platform,
                entry.Origin,
                entry.ComponentId));
        try
        {
            issues.AddRange(NativeDependencyAdmissionValidator
                .Validate(observations, admissions, registry.ComponentIds)
                .Issues);
        }

        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-native-link-manifest",
                "third-party/native-link-manifest.yml"));
            return issues;
        }

        foreach (var entry in entries.Where(entry =>
                     entry.Origin == NativeDependencyOrigin.ThirdParty))
        {
            var issue = NativeArtifactRegistryReader.ValidateDependency(
                root,
                registry,
                entry.ComponentId!,
                entry.InputIdentity,
                entry.Platform);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        foreach (var entry in entries)
        {
            if (!discovered.Any(link =>
                    string.Equals(
                        link.SourcePath,
                        entry.SourcePath,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        link.ConsumerTarget,
                        entry.ConsumerTarget,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        link.InputIdentity,
                        entry.InputIdentity,
                        StringComparison.Ordinal) &&
                    link.InputKind == entry.InputKind &&
                    string.Equals(
                        link.Platform,
                        entry.Platform,
                        StringComparison.Ordinal)))
            {
                issues.Add(new ComplianceIssue(
                    "missing-native-link-callsite",
                    entry.SourcePath));
            }
        }

        return issues;
    }

    private static NativeLinkManifestDocument? ReadManifest(
        string root,
        List<ComplianceIssue> issues)
    {
        var path = Path.Combine(
            root,
            "third-party",
            "native-link-manifest.yml");
        if (!File.Exists(path))
        {
            issues.Add(new ComplianceIssue(
                "missing-native-link-manifest",
                "third-party/native-link-manifest.yml"));
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<NativeLinkManifestDocument>(
                       stream,
                       SerializerOptions) ??
                   throw new JsonException("The native-link manifest is null.");
        }
        catch (JsonException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-native-link-manifest",
                "third-party/native-link-manifest.yml"));
            return null;
        }
    }

    private static void ValidateEntries(
        string root,
        IEnumerable<NativeLinkManifestEntry> entries)
    {
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!TryResolveSourcePath(root, entry.SourcePath, out var sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new InvalidDataException(
                    "A native-link source path is missing or unsafe.");
            }
        }
    }

    private static DiscoveredNativeLink[] DiscoverNativeLinks(string root)
    {
        var sourceRoot = Path.Combine(root, "src");
        var links = new List<DiscoveredNativeLink>();
        if (!Directory.Exists(sourceRoot))
        {
            return [];
        }

        foreach (var path in Directory.EnumerateFiles(
                     sourceRoot,
                     "CMakeLists.txt",
                     SearchOption.AllDirectories))
        {
            var sourcePath = Path.GetRelativePath(root, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            var content = File.ReadAllText(path);
            foreach (Match match in TargetLinkLibrariesRegex().Matches(content))
            {
                var consumer = match.Groups[1].Value;
                var inputs = Regex.Split(match.Groups[2].Value, @"\s+")
                    .Select(token => token.Trim().Trim('"'))
                    .Where(token => !string.IsNullOrWhiteSpace(token) &&
                                    token is not (
                                        "PRIVATE" or "PUBLIC" or "INTERFACE"));
                links.AddRange(inputs.Select(input => new DiscoveredNativeLink(
                    sourcePath,
                    consumer,
                    input,
                    InferInputKind(input),
                    "all")));
            }
        }

        return links.ToArray();
    }

    private static NativeLinkInputKind InferInputKind(string input)
    {
        var extension = Path.GetExtension(input);
        if (string.Equals(extension, ".lib", StringComparison.OrdinalIgnoreCase))
        {
            return NativeLinkInputKind.ImportLibrary;
        }

        if (string.Equals(extension, ".a", StringComparison.OrdinalIgnoreCase))
        {
            return NativeLinkInputKind.StaticLibrary;
        }

        if (extension is not null &&
            (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(extension, ".so", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(extension, ".dylib", StringComparison.OrdinalIgnoreCase)))
        {
            return NativeLinkInputKind.DynamicLibrary;
        }

        return NativeLinkInputKind.ToolchainTarget;
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

    [GeneratedRegex(
        @"target_link_libraries\s*\(\s*([^\s\)]+)(.*?)\)",
        RegexOptions.Singleline)]
    private static partial Regex TargetLinkLibrariesRegex();

    private sealed record NativeLinkManifestDocument(
        int SchemaVersion,
        NativeLinkManifestEntry[] Entries);

    private sealed record NativeLinkManifestEntry(
        string ConsumerTarget,
        string InputIdentity,
        NativeLinkInputKind InputKind,
        string Platform,
        NativeDependencyOrigin Origin,
        string? ComponentId,
        string SourcePath);

    private sealed record DiscoveredNativeLink(
        string SourcePath,
        string ConsumerTarget,
        string InputIdentity,
        NativeLinkInputKind InputKind,
        string Platform);
}
