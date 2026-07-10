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

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(temporaryPath, finalPath, overwrite: false);
        return new FinalizedRecording(finalPath);
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
}
