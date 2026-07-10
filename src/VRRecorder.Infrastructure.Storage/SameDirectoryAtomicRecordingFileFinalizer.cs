using System.Globalization;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class SameDirectoryAtomicRecordingFileFinalizer
    : IRecordingFileFinalizer
{
    public async Task<FinalizedRecording> FinalizeAsync(
        PendingRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);
        cancellationToken.ThrowIfCancellationRequested();

        var temporaryPath = Path.GetFullPath(recording.TemporaryPath);
        var finalPath = Path.GetFullPath(recording.FinalPath);
        EnsureSameDirectory(temporaryPath, finalPath);

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Open,
                         FileAccess.ReadWrite,
                         FileShare.None,
                         bufferSize: 81920,
                         FileOptions.Asynchronous |
                         FileOptions.SequentialScan))
        {
            await stream
                .FlushAsync(cancellationToken)
                .ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        for (var part = 1; ; part++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidatePath = part == 1
                ? finalPath
                : NumberedPath(finalPath, part);
            try
            {
                File.Move(temporaryPath, candidatePath, overwrite: false);
                return new FinalizedRecording(candidatePath);
            }
            catch (IOException) when (File.Exists(candidatePath))
            {
                // A colliding destination is preserved; try the next name.
            }
        }
    }

    private static void EnsureSameDirectory(
        string temporaryPath,
        string finalPath)
    {
        var temporaryDirectory = Path.GetDirectoryName(temporaryPath);
        var finalDirectory = Path.GetDirectoryName(finalPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (temporaryDirectory is null || finalDirectory is null ||
            !string.Equals(
                temporaryDirectory,
                finalDirectory,
                comparison))
        {
            throw new InvalidOperationException(
                "Recording finalization requires a same-directory rename.");
        }
    }

    private static string NumberedPath(string finalPath, int part)
    {
        var directory = Path.GetDirectoryName(finalPath) ??
                        throw new InvalidOperationException(
                            "The final recording has no parent directory.");
        var fileName = Path.GetFileNameWithoutExtension(finalPath);
        var extension = Path.GetExtension(finalPath);
        var suffix = part.ToString("000", CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{fileName}_{suffix}{extension}");
    }
}
