namespace VRRecorder.Application.Recording;

public sealed record RecordingSessionStatistics(
    ulong SourceVideoFrameCount,
    ulong MuxedVideoPacketCount,
    ulong MuxedAudioPacketCount,
    ulong DroppedSourceVideoFrameCount,
    ulong DuplicatedOutputVideoFrameCount,
    TimeSpan LatestEncodeLatency,
    TimeSpan MaximumEncodeLatency,
    TimeSpan AudioVideoOffset);
