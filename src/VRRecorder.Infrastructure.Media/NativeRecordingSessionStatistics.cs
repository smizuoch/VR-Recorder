namespace VRRecorder.Infrastructure.Media;

public sealed record NativeRecordingSessionStatistics(
    ulong SourceVideoFrameCount,
    ulong MuxedVideoPacketCount,
    ulong MuxedAudioPacketCount,
    ulong DroppedSourceVideoFrameCount,
    ulong DuplicatedOutputVideoFrameCount,
    TimeSpan LatestEncodeLatency,
    TimeSpan MaximumEncodeLatency,
    TimeSpan AudioVideoOffset);
