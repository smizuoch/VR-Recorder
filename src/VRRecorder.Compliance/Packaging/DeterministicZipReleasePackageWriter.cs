using System.IO.Compression;
using System.Security.Cryptography;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Packaging;

public sealed class DeterministicZipReleasePackageWriter
    : IReleasePackageWriter
{
    private const int BufferSize = 81920;
    private static readonly DateTimeOffset EntryTimestamp = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    public async Task WriteAsync(
        string packagePath,
        string stagingDirectory,
        StagingInventory inventory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentNullException.ThrowIfNull(inventory);
        if (!Path.IsPathFullyQualified(packagePath))
        {
            throw new ArgumentException(
                "The release package path must be absolute.",
                nameof(packagePath));
        }

        var root = Path.GetFullPath(stagingDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"The staging directory does not exist: {root}");
        }

        EnsureDirectoryAndAncestorsAreNotLinks(root);
        if (inventory.ScanIssues.Count != 0)
        {
            throw new InvalidDataException(
                "A staging inventory with scan issues cannot be packaged.");
        }

        var finalPath = Path.GetFullPath(packagePath);
        if (IsWithinRoot(finalPath, root))
        {
            throw new InvalidDataException(
                "The release package cannot be written inside staging.");
        }

        var outputDirectory = Path.GetDirectoryName(finalPath) ??
                              throw new ArgumentException(
                                  "The release package has no parent directory.",
                                  nameof(packagePath));
        Directory.CreateDirectory(outputDirectory);
        EnsureDirectoryAndAncestorsAreNotLinks(outputDirectory);
        RefuseExistingOutput(finalPath);

        var sources = PrepareSources(root, inventory.Files);
        EnsureInventoryMatchesStaging(root, sources, cancellationToken);
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteTemporaryArchiveAsync(
                    temporaryPath,
                    sources,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureInventoryMatchesStaging(root, sources, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RefuseExistingOutput(finalPath);
            File.Move(temporaryPath, finalPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ArchiveSource[] PrepareSources(
        string root,
        IReadOnlyList<StagedPayloadFile> files)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<ArchiveSource>(files.Count);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            var archivePath = NormalizeArchivePath(file.RelativePath);
            if (!paths.Add(archivePath))
            {
                throw new InvalidDataException(
                    $"The staging inventory contains duplicate path {archivePath}.");
            }

            if (file.Length < 0 || !IsCanonicalSha256(file.Sha256))
            {
                throw new InvalidDataException(
                    $"The staging metadata is invalid for {archivePath}.");
            }

            var fullPath = ResolveWithinRoot(root, archivePath);
            sources.Add(new ArchiveSource(
                archivePath,
                fullPath,
                Convert.FromHexString(file.Sha256),
                file.Length));
        }

        return sources
            .OrderBy(source => source.ArchivePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task WriteTemporaryArchiveAsync(
        string temporaryPath,
        IReadOnlyList<ArchiveSource> sources,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan |
            FileOptions.WriteThrough);
        using (var archive = new ZipArchive(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(
                    source.ArchivePath,
                    CompressionLevel.NoCompression);
                entry.LastWriteTime = EntryTimestamp;
                entry.ExternalAttributes = 0;
                await WriteVerifiedEntryAsync(
                        entry,
                        source,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static async Task WriteVerifiedEntryAsync(
        ZipArchiveEntry entry,
        ArchiveSource source,
        CancellationToken cancellationToken)
    {
        EnsureRegularUnlinkedFile(source.FullPath);
        var file = new FileInfo(source.FullPath);
        if (file.Length != source.ExpectedLength)
        {
            throw CreateFileMismatch(source.ArchivePath);
        }

        await using var input = new FileStream(
            source.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long length = 0;
        while (true)
        {
            var read = await input
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            length += read;
        }

        EnsureRegularUnlinkedFile(source.FullPath);
        var actualHash = hash.GetHashAndReset();
        if (length != source.ExpectedLength ||
            !CryptographicOperations.FixedTimeEquals(
                actualHash,
                source.ExpectedHash))
        {
            throw CreateFileMismatch(source.ArchivePath);
        }
    }

    private static void EnsureInventoryMatchesStaging(
        string root,
        IReadOnlyList<ArchiveSource> sources,
        CancellationToken cancellationToken)
    {
        var actualPaths = EnumerateStagingFiles(root, cancellationToken);
        var expectedPaths = sources
            .Select(source => source.ArchivePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (actualPaths.Count != sources.Count ||
            !actualPaths.SetEquals(expectedPaths))
        {
            throw new InvalidDataException(
                "The staging directory changed after inventory verification.");
        }
    }

    private static HashSet<string> EnumerateStagingFiles(
        string root,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(root));
        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNotLink(directory);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNotLink(entry);
                if (entry is DirectoryInfo child)
                {
                    directories.Push(child);
                    continue;
                }

                if (entry is not FileInfo)
                {
                    throw new InvalidDataException(
                        $"Unsupported staging entry: {entry.FullName}");
                }

                var relativePath = NormalizeArchivePath(
                    Path.GetRelativePath(root, entry.FullName));
                if (!paths.Add(relativePath))
                {
                    throw new InvalidDataException(
                        $"Windows-equivalent staging path is duplicated: {relativePath}");
                }
            }
        }

        return paths;
    }

    private static string NormalizeArchivePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Contains('\0'))
        {
            throw new InvalidDataException($"Unsafe archive path: {path}");
        }

        var normalized = path.Replace('\\', '/');
        if (normalized[0] == '/')
        {
            throw new InvalidDataException($"Unsafe archive path: {path}");
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Any(IsUnsafeSegment))
        {
            throw new InvalidDataException($"Unsafe archive path: {path}");
        }

        return string.Join('/', segments);
    }

    private static bool IsUnsafeSegment(string segment) =>
        string.IsNullOrEmpty(segment) ||
        segment is "." or ".." ||
        segment.EndsWith(' ') ||
        segment.EndsWith('.') ||
        segment.Any(character =>
            character < ' ' || character is '<' or '>' or ':' or '"' or '|' or '?' or '*') ||
        IsWindowsDeviceName(segment);

    private static bool IsWindowsDeviceName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               IsNumberedDevice(name, "COM") ||
               IsNumberedDevice(name, "LPT");
    }

    private static bool IsNumberedDevice(string name, string prefix) =>
        name.Length == 4 &&
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        name[3] is >= '1' and <= '9';

    private static string ResolveWithinRoot(string root, string archivePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            [root, .. archivePath.Split('/')]));
        if (!IsWithinRoot(fullPath, root))
        {
            throw new InvalidDataException(
                $"Archive path escapes staging: {archivePath}");
        }

        return fullPath;
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) +
                         Path.DirectorySeparatorChar;
        return path.StartsWith(rootPrefix, comparison);
    }

    private static void EnsureRegularUnlinkedFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new InvalidDataException($"Staging file is missing: {path}");
        }

        EnsureNotLink(file);
        for (var directory = file.Directory;
             directory is not null;
             directory = directory.Parent)
        {
            EnsureNotLink(directory);
        }
    }

    private static void EnsureDirectoryAndAncestorsAreNotLinks(string path)
    {
        for (var directory = new DirectoryInfo(path);
             directory is not null;
             directory = directory.Parent)
        {
            if (!directory.Exists)
            {
                throw new DirectoryNotFoundException(
                    $"Directory does not exist: {directory.FullName}");
            }

            EnsureNotLink(directory);
        }
    }

    private static void EnsureNotLink(FileSystemInfo entry)
    {
        entry.Refresh();
        if (entry.LinkTarget is not null ||
            entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException(
                $"Symbolic links are not allowed in package paths: {entry.FullName}");
        }
    }

    private static void RefuseExistingOutput(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        if (file.Exists || directory.Exists ||
            file.LinkTarget is not null || directory.LinkTarget is not null)
        {
            throw new IOException(
                $"The release package already exists: {path}");
        }
    }

    private static bool IsCanonicalSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InvalidDataException CreateFileMismatch(string path) =>
        new($"The staged file no longer matches its verified inventory: {path}");

    private sealed record ArchiveSource(
        string ArchivePath,
        string FullPath,
        byte[] ExpectedHash,
        long ExpectedLength);
}
