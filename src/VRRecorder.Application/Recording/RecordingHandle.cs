using VRRecorder.Domain.Timing;

namespace VRRecorder.Application.Recording;

public sealed record RecordingHandle(
    string Id,
    MonotonicTimestamp FirstPacketCommittedAt);
