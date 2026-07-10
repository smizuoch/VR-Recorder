using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface IStaleRecordingCatalog
{
    Task<IReadOnlyList<RecoverableRecording>> FindAsync(
        string outputDirectory,
        CancellationToken cancellationToken);
}
