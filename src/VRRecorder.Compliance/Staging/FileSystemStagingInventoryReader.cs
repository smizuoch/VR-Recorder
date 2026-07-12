using System.Buffers.Binary;
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
                var kind = await ClassifyAsync(
                        file.Extension,
                        stream,
                        cancellationToken)
                    .ConfigureAwait(false);
                var hash = await SHA256
                    .HashDataAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                files.Add(new StagedPayloadFile(
                    relativePath,
                    Convert.ToHexString(hash).ToLowerInvariant(),
                    file.Length,
                    kind));
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

    private static async ValueTask<StagedArtifactKind> ClassifyAsync(
        string extension,
        Stream stream,
        CancellationToken cancellationToken)
    {
        StagedArtifactKind? extensionKind = extension.ToUpperInvariant() switch
        {
            ".DLL" => StagedArtifactKind.NativeLibrary,
            ".EXE" => StagedArtifactKind.Executable,
            _ => null,
        };
        if (extensionKind is not null)
        {
            return extensionKind.Value;
        }

        var dosHeader = new byte[64];
        if (!await TryReadExactlyAsync(
                stream,
                dosHeader,
                cancellationToken)
            .ConfigureAwait(false) ||
            dosHeader[0] != (byte)'M' ||
            dosHeader[1] != (byte)'Z')
        {
            stream.Position = 0;
            return StagedArtifactKind.Asset;
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(
            dosHeader.AsSpan(0x3c, sizeof(int)));
        if (peOffset < dosHeader.Length ||
            peOffset > stream.Length - 24)
        {
            stream.Position = 0;
            return StagedArtifactKind.Asset;
        }

        stream.Position = peOffset;
        var peHeader = new byte[24];
        if (!await TryReadExactlyAsync(
                stream,
                peHeader,
                cancellationToken)
            .ConfigureAwait(false) ||
            peHeader[0] != (byte)'P' ||
            peHeader[1] != (byte)'E' ||
            peHeader[2] != 0 ||
            peHeader[3] != 0)
        {
            stream.Position = 0;
            return StagedArtifactKind.Asset;
        }

        const ushort imageFileDll = 0x2000;
        var characteristics = BinaryPrimitives.ReadUInt16LittleEndian(
            peHeader.AsSpan(22, sizeof(ushort)));
        stream.Position = 0;
        return (characteristics & imageFileDll) != 0
            ? StagedArtifactKind.NativeLibrary
            : StagedArtifactKind.Executable;
    }

    private static async ValueTask<bool> TryReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(
                    buffer[read..],
                    cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }
}
