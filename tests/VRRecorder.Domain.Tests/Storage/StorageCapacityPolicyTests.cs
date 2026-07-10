using VRRecorder.Domain.Storage;

namespace VRRecorder.Domain.Tests.Storage;

public sealed class StorageCapacityPolicyTests
{
    [Theory]
    [InlineData(2_147_483_648, true)]
    [InlineData(2_147_483_647, false)]
    public void StartRequiresAtLeastTwoGibibytes(long bytes, bool expected)
    {
        Assert.Equal(expected, StorageCapacityPolicy.CanStart(new StorageSpace(bytes)));
    }

    [Theory]
    [InlineData(536_870_912, RecordingStorageState.Healthy)]
    [InlineData(536_870_911, RecordingStorageState.Warning)]
    [InlineData(268_435_456, RecordingStorageState.Warning)]
    [InlineData(268_435_455, RecordingStorageState.StopRequired)]
    public void RuntimeStateUsesExactSafetyBoundaries(
        long bytes,
        RecordingStorageState expected)
    {
        Assert.Equal(expected, StorageCapacityPolicy.Classify(new StorageSpace(bytes)));
    }

    [Fact]
    public void RemainingTimeExcludesSafeStopReserve()
    {
        var available = new StorageSpace(368_435_456);

        var remaining = StorageCapacityPolicy.EstimateRemaining(
            available,
            estimatedBytesPerSecond: 10_000_000);

        Assert.Equal(TimeSpan.FromSeconds(10), remaining);
    }
}
