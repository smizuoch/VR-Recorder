using VRRecorder.Application.Recording;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingStorageMonitor
{
    Task RunAsync(
        RecordingHandle handle,
        OutputPath outputPath,
        CancellationToken cancellationToken);
}
