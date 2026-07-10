namespace VRRecorder.Compliance.Staging;

public interface IStagingInventoryReader
{
    Task<StagingInventory> ReadAsync(
        string stagingDirectory,
        CancellationToken cancellationToken);
}
