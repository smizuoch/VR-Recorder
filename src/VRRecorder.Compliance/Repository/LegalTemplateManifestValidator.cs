using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Repository;

public static class LegalTemplateManifestValidator
{
    private const string TemplateDirectoryName = "legal-template";
    private const string ManifestFileName = "EXAMPLE-LEGAL-MANIFEST.sha256";
    private const string RequiredHeader =
        "# DESIGN-TIME SNAPSHOT — production release regenerates this manifest from the final Legal Bundle.";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<ComplianceIssue> Verify(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var issues = new List<ComplianceIssue>();
        var templateRoot = Path.Combine(
            Path.GetFullPath(repositoryRoot),
            TemplateDirectoryName);
        var manifestPath = Path.Combine(templateRoot, ManifestFileName);
        if (!Directory.Exists(templateRoot))
        {
            return
            [
                new ComplianceIssue(
                    "missing-legal-template-directory",
                    TemplateDirectoryName),
            ];
        }

        if (!File.Exists(manifestPath))
        {
            return
            [
                new ComplianceIssue(
                    "missing-legal-template-manifest",
                    Subject(ManifestFileName)),
            ];
        }

        if (IsReparsePoint(templateRoot) || IsReparsePoint(manifestPath))
        {
            return
            [
                new ComplianceIssue(
                    "legal-template-symbolic-link",
                    Subject(ManifestFileName)),
            ];
        }

        var entries = ReadManifest(manifestPath, issues);
        var inventory = ReadInventory(templateRoot, issues);
        VerifyEntries(entries, inventory, issues);

        return issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
    }

    private static List<ManifestEntry> ReadManifest(
        string manifestPath,
        List<ComplianceIssue> issues)
    {
        string manifest;
        try
        {
            manifest = StrictUtf8.GetString(File.ReadAllBytes(manifestPath));
        }
        catch (DecoderFallbackException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-legal-template-manifest-encoding",
                Subject(ManifestFileName)));
            return [];
        }
        catch (IOException)
        {
            issues.Add(new ComplianceIssue(
                "unreadable-legal-template-manifest",
                Subject(ManifestFileName)));
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(new ComplianceIssue(
                "unreadable-legal-template-manifest",
                Subject(ManifestFileName)));
            return [];
        }

        if (manifest.Length == 0 ||
            !manifest.EndsWith('\n') ||
            manifest.Contains('\r'))
        {
            issues.Add(new ComplianceIssue(
                "invalid-legal-template-manifest-format",
                Subject(ManifestFileName)));
            return [];
        }

        var lines = manifest.Split('\n');
        if (!string.Equals(lines[0], RequiredHeader, StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "invalid-legal-template-manifest-header",
                Subject(ManifestFileName)));
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ManifestEntry>();
        string? previousPath = null;
        for (var index = 1; index < lines.Length - 1; index++)
        {
            var lineNumber = index + 1;
            if (!TryParseEntry(lines[index], out var entry))
            {
                issues.Add(new ComplianceIssue(
                    "invalid-legal-template-manifest-entry",
                    $"{Subject(ManifestFileName)}:{lineNumber}"));
                continue;
            }

            if (string.Equals(
                    entry.RelativePath,
                    ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ComplianceIssue(
                    "invalid-legal-template-manifest-entry",
                    $"{Subject(ManifestFileName)}:{lineNumber}"));
                continue;
            }

            if (!paths.Add(entry.RelativePath))
            {
                issues.Add(new ComplianceIssue(
                    "duplicate-legal-template-manifest-path",
                    Subject(entry.RelativePath)));
                continue;
            }

            if (previousPath is not null &&
                StringComparer.Ordinal.Compare(
                    previousPath,
                    entry.RelativePath) >= 0)
            {
                issues.Add(new ComplianceIssue(
                    "noncanonical-legal-template-manifest-order",
                    Subject(entry.RelativePath)));
            }

            previousPath = entry.RelativePath;
            entries.Add(entry);
        }

        return entries;
    }

    private static bool TryParseEntry(
        string line,
        out ManifestEntry entry)
    {
        entry = default!;
        if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
        {
            return false;
        }

        var hash = line[..64];
        if (hash.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            return false;
        }

        var relativePath = line[66..];
        if (relativePath.Length == 0 ||
            relativePath.Any(char.IsControl) ||
            relativePath.Split('/').Any(segment =>
                segment.Length == 0 ||
                char.IsWhiteSpace(segment[0]) ||
                char.IsWhiteSpace(segment[^1])))
        {
            return false;
        }

        try
        {
            _ = LegalArtifactPath.Resolve(
                Environment.CurrentDirectory,
                relativePath);
        }
        catch (ArgumentException)
        {
            return false;
        }

        entry = new ManifestEntry(relativePath, hash);
        return true;
    }

    private static TemplateInventory ReadInventory(
        string templateRoot,
        List<ComplianceIssue> issues)
    {
        var files = new Dictionary<string, InventoryFile>(
            StringComparer.OrdinalIgnoreCase);
        var unsafePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(templateRoot);

        try
        {
            while (pendingDirectories.TryPop(out var directory))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var relativePath = ToManifestPath(templateRoot, path);
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        unsafePaths.Add(relativePath);
                        issues.Add(new ComplianceIssue(
                            "legal-template-symbolic-link",
                            Subject(relativePath)));
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(path);
                        continue;
                    }

                    if (string.Equals(
                            relativePath,
                            ManifestFileName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        _ = LegalArtifactPath.Resolve(templateRoot, relativePath);
                    }
                    catch (ArgumentException)
                    {
                        issues.Add(new ComplianceIssue(
                            "invalid-legal-template-file-path",
                            Subject(relativePath)));
                        continue;
                    }

                    if (!files.TryAdd(
                            relativePath,
                            new InventoryFile(relativePath, path)))
                    {
                        issues.Add(new ComplianceIssue(
                            "duplicate-legal-template-file-path",
                            Subject(relativePath)));
                    }
                }
            }
        }
        catch (IOException)
        {
            issues.Add(new ComplianceIssue(
                "unreadable-legal-template-inventory",
                TemplateDirectoryName));
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(new ComplianceIssue(
                "unreadable-legal-template-inventory",
                TemplateDirectoryName));
        }

        return new TemplateInventory(files, unsafePaths);
    }

    private static void VerifyEntries(
        IReadOnlyCollection<ManifestEntry> entries,
        TemplateInventory inventory,
        List<ComplianceIssue> issues)
    {
        var registeredPaths = entries
            .Select(entry => entry.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (inventory.UnsafePaths.Any(path =>
                    string.Equals(
                        path,
                        entry.RelativePath,
                        StringComparison.OrdinalIgnoreCase) ||
                    entry.RelativePath.StartsWith(
                        $"{path}/",
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!inventory.Files.TryGetValue(
                    entry.RelativePath,
                    out var inventoryFile))
            {
                issues.Add(new ComplianceIssue(
                    "missing-legal-template-file",
                    Subject(entry.RelativePath)));
                continue;
            }

            if (!string.Equals(
                    entry.RelativePath,
                    inventoryFile.RelativePath,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "noncanonical-legal-template-manifest-path",
                    Subject(entry.RelativePath)));
            }

            try
            {
                using var stream = File.OpenRead(inventoryFile.FullPath);
                var actualHash = Convert
                    .ToHexString(SHA256.HashData(stream))
                    .ToLowerInvariant();
                if (!string.Equals(
                        entry.Sha256,
                        actualHash,
                        StringComparison.Ordinal))
                {
                    issues.Add(new ComplianceIssue(
                        "legal-template-hash-mismatch",
                        Subject(entry.RelativePath)));
                }
            }
            catch (IOException)
            {
                issues.Add(new ComplianceIssue(
                    "unreadable-legal-template-file",
                    Subject(entry.RelativePath)));
            }
            catch (UnauthorizedAccessException)
            {
                issues.Add(new ComplianceIssue(
                    "unreadable-legal-template-file",
                    Subject(entry.RelativePath)));
            }
        }

        foreach (var relativePath in inventory.Files.Keys
                     .Where(path => !registeredPaths.Contains(path)))
        {
            issues.Add(new ComplianceIssue(
                "unregistered-legal-template-file",
                Subject(relativePath)));
        }
    }

    private static string ToManifestPath(string root, string path) => Path
        .GetRelativePath(root, path)
        .Replace(Path.DirectorySeparatorChar, '/')
        .Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string Subject(string relativePath) =>
        $"{TemplateDirectoryName}/{relativePath}";

    private sealed record ManifestEntry(string RelativePath, string Sha256);

    private sealed record TemplateInventory(
        Dictionary<string, InventoryFile> Files,
        HashSet<string> UnsafePaths);

    private sealed record InventoryFile(string RelativePath, string FullPath);
}
