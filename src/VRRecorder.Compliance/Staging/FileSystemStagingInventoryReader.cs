using System.Security.Cryptography;

namespace VRRecorder.Compliance.Staging;

public sealed class FileSystemStagingInventoryReader : IStagingInventoryReader
{
    public async Task<StagingInventory> ReadAsync(
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        var root = Path.GetFullPath(stagingDirectory);
        if (!Directory.Exists(root))
        {
            return new StagingInventory(
                [],
                [new ComplianceIssue("missing-staging-directory", root)]);
        }

        var files = new List<StagedPayloadFile>();
        var issues = new List<ComplianceIssue>();
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(root));

        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in directory
                         .EnumerateFileSystemInfos()
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = NormalizeRelativePath(
                    Path.GetRelativePath(root, entry.FullName));
                if (IsLink(entry))
                {
                    issues.Add(new ComplianceIssue(
                        "staging-link-not-allowed",
                        relativePath));
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Push(childDirectory);
                    continue;
                }

                if (entry is not FileInfo file)
                {
                    continue;
                }

                await using var stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256
                    .HashDataAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                files.Add(new StagedPayloadFile(
                    relativePath,
                    Convert.ToHexString(hash).ToLowerInvariant(),
                    file.Length,
                    Classify(file.Extension)));
            }
        }

        return new StagingInventory(
            files
                .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            issues
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.Subject, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsLink(FileSystemInfo entry) =>
        entry.LinkTarget is not null ||
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static StagedArtifactKind Classify(string extension) =>
        extension.ToUpperInvariant() switch
        {
            ".DLL" => StagedArtifactKind.NativeLibrary,
            ".EXE" => StagedArtifactKind.Executable,
            _ => StagedArtifactKind.Asset,
        };
}
