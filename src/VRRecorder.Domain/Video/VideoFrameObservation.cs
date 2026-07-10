using VRRecorder.Domain.Timing;

namespace VRRecorder.Domain.Video;

public sealed record VideoFrameObservation
{
    public VideoFrameObservation(
        MonotonicTimestamp receivedAt,
        bool isBlack)
    {
        ReceivedAt = receivedAt;
        IsBlack = isBlack;
    }

    public MonotonicTimestamp ReceivedAt { get; }

    public bool IsBlack { get; }
}
