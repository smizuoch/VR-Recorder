using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class FileSystemStaleRecordingCatalog : IStaleRecordingCatalog
{
    private const string StaleSuffix = ".recording.mp4";

    public Task<IReadOnlyList<RecoverableRecording>> FindAsync(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(outputDirectory);
        var recordings = new List<RecoverableRecording>();
        foreach (var path in Directory
                     .EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!path.EndsWith(StaleSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var file = new FileInfo(path);
            if (file.LinkTarget is not null ||
                file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            recordings.Add(new RecoverableRecording(file.FullName));
        }

        return Task.FromResult<IReadOnlyList<RecoverableRecording>>(recordings);
    }
}
