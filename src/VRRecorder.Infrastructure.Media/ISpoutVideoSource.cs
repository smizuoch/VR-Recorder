using VRRecorder.Application.Recording;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Infrastructure.Media;

public interface ISpoutVideoSource
{
    Task<IReadOnlyList<SpoutSenderSnapshot>> SnapshotAsync(
        CancellationToken cancellationToken);

    IAsyncEnumerable<SpoutFrameObservation> ObserveFramesAsync(
        CancellationToken cancellationToken);
}

public sealed record SpoutSenderSnapshot
{
    public SpoutSenderSnapshot(string senderId, ulong latestFrameSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);
        SenderId = senderId;
        LatestFrameSequence = latestFrameSequence;
    }

    public string SenderId { get; }

    public ulong LatestFrameSequence { get; }
}

public sealed record SpoutFrameObservation
{
    public SpoutFrameObservation(
        StableVideoSignal signal,
        ulong frameSequence,
        MonotonicTimestamp receivedAt)
    {
        ArgumentNullException.ThrowIfNull(signal);
        Signal = signal;
        FrameSequence = frameSequence;
        ReceivedAt = receivedAt;
    }

    public StableVideoSignal Signal { get; init; }

    public ulong FrameSequence { get; init; }

    public MonotonicTimestamp ReceivedAt { get; init; }
}
