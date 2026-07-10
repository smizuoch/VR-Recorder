using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Recording;

public sealed record RecordingStorageSnapshot(
    StorageSpace AvailableSpace,
    RecordingStorageState State,
    TimeSpan EstimatedRemaining);
