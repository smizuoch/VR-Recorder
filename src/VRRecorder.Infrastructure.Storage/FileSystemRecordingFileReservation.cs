using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class FileSystemRecordingFileReservation
    : IRecordingFileReservation
{
    public Task<PendingRecording> ReserveAsync(
        OutputPath outputPath,
        RecordingFileDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        for (var ordinal = 1; ordinal < int.MaxValue; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var names = RecordingFileNamePolicy.Create(descriptor, ordinal);
            var temporaryPath = Path.Combine(
                outputPath.FullPath,
                names.TemporaryFileName);
            var finalPath = Path.Combine(
                outputPath.FullPath,
                names.FinalFileName);

            if (File.Exists(finalPath))
            {
                continue;
            }

            try
            {
                using (var stream = new FileStream(
                               temporaryPath,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None))
                {
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(finalPath))
                {
                    File.Delete(temporaryPath);
                    continue;
                }

                return Task.FromResult(
                    new PendingRecording(temporaryPath, finalPath));
            }
            catch (IOException) when (
                File.Exists(temporaryPath) || File.Exists(finalPath))
            {
                // Another reservation won this ordinal.
            }
        }

        throw new IOException("No recording filename could be reserved.");
    }
}
