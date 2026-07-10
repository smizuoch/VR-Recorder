using System.Net;
using System.Text;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Runtime;

public sealed class AuthenticatedLegalBundleMirror
{
    private const int BufferSize = 81920;
    private const string LegalDirectoryName = "VR-Recorder-Legal";
    private const string CurrentFileName = "CURRENT.txt";
    private const string IndexFileName = "OPEN-NOTICES.html";
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly AuthenticatedLegalBundleVerifier _verifier;

    public AuthenticatedLegalBundleMirror(
        AuthenticatedLegalBundleVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        _verifier = verifier;
    }

    public async Task<LegalBundleIdentity> MirrorAsync(
        string sourceBundleDirectory,
        string outputDirectory,
        string productVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBundleDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ValidateProductVersion(productVersion);

        var sourceRoot = Path.GetFullPath(sourceBundleDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"The authenticated Legal Bundle is missing: {sourceRoot}");
        }

        EnsureDirectoryAndAncestorsAreNotLinks(sourceRoot);
        if (PathsOverlap(sourceRoot, outputRoot))
        {
            throw new InvalidDataException(
                "The Legal Bundle source and recording output must not overlap.");
        }

        var sourceIdentity = await RequireVerifiedAsync(
                sourceRoot,
                cancellationToken)
            .ConfigureAwait(false);
        var sourceFiles = EnumerateFilesWithoutLinks(
            sourceRoot,
            cancellationToken);

        EnsureExistingAncestorsAreNotLinks(outputRoot);
        Directory.CreateDirectory(outputRoot);
        EnsureDirectoryAndAncestorsAreNotLinks(outputRoot);
        var legalRoot = Path.Combine(outputRoot, LegalDirectoryName);
        RefuseFileOrLink(legalRoot);
        Directory.CreateDirectory(legalRoot);
        EnsureTreeContainsNoLinks(legalRoot, cancellationToken);

        var currentPath = Path.Combine(legalRoot, CurrentFileName);
        var indexPath = Path.Combine(legalRoot, IndexFileName);
        EnsureReplaceableControlFile(currentPath);
        EnsureReplaceableControlFile(indexPath);

        var versionDirectory = Path.Combine(legalRoot, productVersion);
        await EnsureVersionMirrorAsync(
                sourceRoot,
                sourceFiles,
                sourceIdentity,
                versionDirectory,
                legalRoot,
                productVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var mirroredIdentity = await RequireVerifiedAsync(
                versionDirectory,
                cancellationToken)
            .ConfigureAwait(false);
        if (mirroredIdentity != sourceIdentity)
        {
            throw new InvalidDataException(
                "The mirrored Legal Bundle identity changed during publication.");
        }

        var mirroredFiles = EnumerateFilesWithoutLinks(
            versionDirectory,
            cancellationToken);
        var indexBytes = CreateOfflineIndex(
            productVersion,
            mirroredFiles.Select(file => file.RelativePath));
        var currentBytes = Utf8.GetBytes($"{productVersion}/\n");

        await WriteAtomicFileAsync(
                indexPath,
                indexBytes,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicFileAsync(
                currentPath,
                currentBytes,
                cancellationToken)
            .ConfigureAwait(false);
        return sourceIdentity;
    }

    private async Task EnsureVersionMirrorAsync(
        string sourceRoot,
        IReadOnlyList<BundleFile> sourceFiles,
        LegalBundleIdentity sourceIdentity,
        string versionDirectory,
        string legalRoot,
        string productVersion,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(versionDirectory))
        {
            EnsureTreeContainsNoLinks(versionDirectory, cancellationToken);
            var existingIdentity = await RequireVerifiedAsync(
                    versionDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingIdentity != sourceIdentity)
            {
                throw new InvalidDataException(
                    $"Legal Bundle version {productVersion} already has a different identity.");
            }

            return;
        }

        RefuseFileOrLink(versionDirectory);
        var stagingDirectory = Path.Combine(
            legalRoot,
            $".{productVersion}.staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            foreach (var source in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = LegalArtifactPath.Resolve(
                    stagingDirectory,
                    source.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyVerifiedSourceFileAsync(
                        sourceRoot,
                        source,
                        destination,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var stagedIdentity = await RequireVerifiedAsync(
                    stagingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            if (stagedIdentity != sourceIdentity)
            {
                throw new InvalidDataException(
                    "The staged Legal Bundle identity does not match its authenticated source.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, versionDirectory);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private async Task<LegalBundleIdentity> RequireVerifiedAsync(
        string bundleDirectory,
        CancellationToken cancellationToken)
    {
        var verification = await _verifier
            .VerifyAsync(bundleDirectory, cancellationToken)
            .ConfigureAwait(false);
        return verification switch
        {
            LegalBundleVerification.Verified verified => verified.Identity,
            LegalBundleVerification.Rejected rejected =>
                throw new InvalidDataException(
                    "Legal Bundle authentication failed: " +
                    string.Join(
                        ", ",
                        rejected.Issues.Select(issue => issue.Code))),
            _ => throw new InvalidOperationException(
                "Unknown Legal Bundle verification result."),
        };
    }

    private static BundleFile[] EnumerateFilesWithoutLinks(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new List<BundleFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(root));
        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNotLink(directory);
            foreach (var entry in directory
                         .EnumerateFileSystemInfos()
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
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
                        $"Unsupported Legal Bundle entry: {entry.FullName}");
                }

                var relativePath = Path
                    .GetRelativePath(root, entry.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                _ = LegalArtifactPath.Resolve(root, relativePath);
                if (!paths.Add(relativePath))
                {
                    throw new InvalidDataException(
                        $"Windows-equivalent Legal Bundle path is duplicated: {relativePath}");
                }

                files.Add(new BundleFile(relativePath, entry.FullName));
            }
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task CopyVerifiedSourceFileAsync(
        string sourceRoot,
        BundleFile source,
        string destination,
        CancellationToken cancellationToken)
    {
        EnsureSourceFileIsUnlinked(sourceRoot, source);
        await using var input = new FileStream(
            source.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan |
            FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
        EnsureSourceFileIsUnlinked(sourceRoot, source);
    }

    private static void EnsureSourceFileIsUnlinked(
        string sourceRoot,
        BundleFile source)
    {
        var expectedPath = LegalArtifactPath.Resolve(
            sourceRoot,
            source.RelativePath);
        if (!string.Equals(
                expectedPath,
                source.FullPath,
                PathComparison))
        {
            throw new InvalidDataException(
                $"Legal Bundle path changed during mirroring: {source.RelativePath}");
        }

        var file = new FileInfo(expectedPath);
        if (!file.Exists)
        {
            throw new InvalidDataException(
                $"Legal Bundle payload disappeared: {source.RelativePath}");
        }

        EnsureNotLink(file);
    }

    private static byte[] CreateOfflineIndex(
        string productVersion,
        IEnumerable<string> relativePaths)
    {
        var output = new StringBuilder()
            .Append("<!DOCTYPE html>\n")
            .Append("<html lang=\"en\">\n<head>\n")
            .Append("<meta charset=\"utf-8\" />\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'\" />\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n")
            .Append("<title>VR-Recorder legal notices</title>\n")
            .Append("</head>\n<body>\n")
            .Append("<h1>VR-Recorder legal notices</h1>\n")
            .Append("<p>Current Legal Bundle: <code>")
            .Append(WebUtility.HtmlEncode(productVersion))
            .Append("</code></p>\n")
            .Append("<nav aria-label=\"Legal Bundle files\">\n<ul>\n");
        foreach (var relativePath in relativePaths
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var link = string.Join(
                '/',
                productVersion
                    .Split('/')
                    .Concat(relativePath.Split('/'))
                    .Select(Uri.EscapeDataString));
            output.Append("<li><a href=\"")
                .Append(WebUtility.HtmlEncode(link))
                .Append("\">")
                .Append(WebUtility.HtmlEncode(relativePath))
                .Append("</a></li>\n");
        }

        return Utf8.GetBytes(output
            .Append("</ul>\n</nav>\n</body>\n</html>\n")
            .ToString());
    }

    private static async Task WriteAtomicFileAsync(
        string targetPath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        EnsureReplaceableControlFile(targetPath);
        var directory = Path.GetDirectoryName(targetPath) ??
                        throw new InvalidOperationException(
                            "A Legal Bundle control file has no parent directory.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous |
                             FileOptions.SequentialScan |
                             FileOptions.WriteThrough))
            {
                await output
                    .WriteAsync(content, cancellationToken)
                    .ConfigureAwait(false);
                await output
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureReplaceableControlFile(targetPath);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateProductVersion(string productVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        if (productVersion.Length > 128 ||
            productVersion is "." or ".." ||
            productVersion.Any(character =>
                character is not (
                    >= '0' and <= '9' or
                    >= 'A' and <= 'Z' or
                    >= 'a' and <= 'z' or
                    '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "The product version must be a package-safe directory name.",
                nameof(productVersion));
        }
    }

    private static void EnsureTreeContainsNoLinks(
        string root,
        CancellationToken cancellationToken)
    {
        _ = EnumerateFilesWithoutLinks(root, cancellationToken);
    }

    private static void EnsureReplaceableControlFile(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        file.Refresh();
        directory.Refresh();
        if (directory.Exists || file.LinkTarget is not null ||
            directory.LinkTarget is not null ||
            (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            throw new InvalidDataException(
                $"Unsafe Legal Bundle control path: {path}");
        }
    }

    private static void RefuseFileOrLink(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        file.Refresh();
        directory.Refresh();
        if (file.Exists || file.LinkTarget is not null ||
            directory.LinkTarget is not null)
        {
            throw new InvalidDataException(
                $"Unsafe Legal Bundle directory path: {path}");
        }
    }

    private static void EnsureExistingAncestorsAreNotLinks(string path)
    {
        for (var directory = new DirectoryInfo(path);
             directory is not null;
             directory = directory.Parent)
        {
            directory.Refresh();
            if (directory.LinkTarget is not null ||
                (directory.Exists &&
                 directory.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                throw new InvalidDataException(
                    $"Symbolic links are not allowed in Legal Bundle paths: {directory.FullName}");
            }
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
                $"Symbolic links are not allowed in Legal Bundle paths: {entry.FullName}");
        }
    }

    private static bool PathsOverlap(string first, string second) =>
        IsWithinOrEqual(first, second) || IsWithinOrEqual(second, first);

    private static bool IsWithinOrEqual(string path, string root)
    {
        if (string.Equals(path, root, PathComparison))
        {
            return true;
        }

        var rootPrefix = Path.TrimEndingDirectorySeparator(root) +
                         Path.DirectorySeparatorChar;
        return path.StartsWith(rootPrefix, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed record BundleFile(string RelativePath, string FullPath);
}
