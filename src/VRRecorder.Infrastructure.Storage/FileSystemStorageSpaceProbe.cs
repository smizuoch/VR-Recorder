using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class FileSystemStorageSpaceProbe : IStorageSpaceProbe
{
    public Task<StorageSpace> MeasureAsync(
        OutputPath outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(outputPath.FullPath))
        {
            throw new DirectoryNotFoundException(
                $"The output directory does not exist: {outputPath.FullPath}");
        }

        var root = Path.GetPathRoot(outputPath.FullPath) ??
                   throw new IOException(
                       "The output directory has no filesystem root.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady)
        {
            throw new IOException(
                $"The output filesystem is not ready: {root}");
        }

        return Task.FromResult(new StorageSpace(drive.AvailableFreeSpace));
    }
}
