namespace VRRecorder.Domain.Storage;

public static class StorageCapacityPolicy
{
    public const long MinimumStartBytes = 2_147_483_648;
    public const long WarningBelowBytes = 536_870_912;
    public const long StopBelowBytes = 268_435_456;

    public static TimeSpan MonitorInterval { get; } = TimeSpan.FromSeconds(5);

    public static bool CanStart(StorageSpace space) =>
        space.AvailableBytes >= MinimumStartBytes;

    public static RecordingStorageState Classify(StorageSpace space)
    {
        if (space.AvailableBytes < StopBelowBytes)
        {
            return RecordingStorageState.StopRequired;
        }

        return space.AvailableBytes < WarningBelowBytes
            ? RecordingStorageState.Warning
            : RecordingStorageState.Healthy;
    }

    public static TimeSpan EstimateRemaining(
        StorageSpace space,
        long estimatedBytesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            estimatedBytesPerSecond);
        var usableBytes = Math.Max(
            0,
            space.AvailableBytes - StopBelowBytes);
        return TimeSpan.FromSeconds(
            (double)usableBytes / estimatedBytesPerSecond);
    }
}
