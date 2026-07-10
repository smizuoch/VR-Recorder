using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Infrastructure.Storage;

public sealed class FileSystemRecordingRecoveryStore : IRecordingRecoveryStore
{
    private const string RecoveryDirectoryName = "VR-Recorder-Recovery";

    public Task<QuarantinedRecording> QuarantineAsync(
        RecoverableRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.GetFullPath(recording.SourcePath);
        var outputDirectory = Path.GetDirectoryName(sourcePath) ??
                              throw new InvalidOperationException(
                                  "The recoverable recording has no parent directory.");
        var recoveryDirectory = Path.Combine(
            outputDirectory,
            RecoveryDirectoryName);
        Directory.CreateDirectory(recoveryDirectory);
        var recoveryPath = Path.Combine(
            recoveryDirectory,
            Path.GetFileName(sourcePath));

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(sourcePath, recoveryPath, overwrite: false);
        return Task.FromResult(new QuarantinedRecording(
            Path.GetFullPath(recoveryPath)));
    }
}
