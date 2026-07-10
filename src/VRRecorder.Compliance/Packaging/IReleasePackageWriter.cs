using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Packaging;

public interface IReleasePackageWriter
{
    Task WriteAsync(
        string packagePath,
        StagingInventory inventory,
        CancellationToken cancellationToken);
}
