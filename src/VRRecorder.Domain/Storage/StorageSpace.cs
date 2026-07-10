namespace VRRecorder.Domain.Storage;

public readonly record struct StorageSpace
{
    public StorageSpace(long availableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(availableBytes);
        AvailableBytes = availableBytes;
    }

    public long AvailableBytes { get; }
}
