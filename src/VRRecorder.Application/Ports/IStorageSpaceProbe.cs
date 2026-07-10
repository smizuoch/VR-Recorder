using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Ports;

public interface IStorageSpaceProbe
{
    Task<StorageSpace> MeasureAsync(
        OutputPath outputPath,
        CancellationToken cancellationToken);
}
