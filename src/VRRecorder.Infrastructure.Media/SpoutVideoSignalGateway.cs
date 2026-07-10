using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public sealed class SpoutVideoSignalGateway : IVideoSignalGateway
{
    private static readonly TimeSpan StabilityDuration =
        TimeSpan.FromMilliseconds(300);
    private const int StableFrameCount = 3;
    private readonly object _gate = new();
    private readonly ISpoutVideoSource _source;
    private IReadOnlyDictionary<string, ulong>? _baseline;

    public SpoutVideoSignalGateway(ISpoutVideoSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public async Task CaptureBaselineAsync(CancellationToken cancellationToken)
    {
        var senders = await _source
            .SnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(senders);
        var baseline = senders.ToDictionary(
            sender => sender.SenderId,
            sender => sender.LatestFrameSequence,
            StringComparer.Ordinal);
        lock (_gate)
        {
            _baseline = baseline;
        }
    }

    public async Task<StableVideoSignal> WaitForStableSignalAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The stable-signal timeout must be positive and finite.");
        }

        IReadOnlyDictionary<string, ulong> baseline;
        lock (_gate)
        {
            baseline = _baseline ?? throw new InvalidOperationException(
                "A sender baseline must be captured before observing frames.");
            _baseline = null;
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        try
        {
            await foreach (var frame in _source
                               .ObserveFramesAsync(operationCancellation.Token)
                               .WithCancellation(operationCancellation.Token)
                               .ConfigureAwait(false))
            {
                if (!frame.Signal.HasDiscoveredSourceIdentity)
                {
                    throw new InvalidDataException(
                        "A Spout frame must carry discovered sender and adapter identity.");
                }

                if (baseline.TryGetValue(
                        frame.Signal.SenderId,
                        out var baselineSequence) &&
                    frame.FrameSequence <= baselineSequence)
                {
                    candidates.Remove(frame.Signal.SenderId);
                    continue;
                }

                if (!candidates.TryGetValue(frame.Signal.SenderId, out var candidate) ||
                    !candidate.HasSameSignature(frame.Signal))
                {
                    candidates[frame.Signal.SenderId] = new Candidate(frame);
                    continue;
                }

                if (frame.FrameSequence <= candidate.LastFrameSequence ||
                    frame.ReceivedAt.Elapsed < candidate.LastReceivedAt.Elapsed)
                {
                    candidates.Remove(frame.Signal.SenderId);
                    continue;
                }

                candidate.Observe(frame);
                if (candidate.FrameCount >= StableFrameCount &&
                    frame.ReceivedAt.Elapsed - candidate.FirstReceivedAt.Elapsed >=
                    StabilityDuration)
                {
                    return frame.Signal;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "No Spout sender produced a stable video signal before the timeout.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(
            "The Spout frame source completed before a stable signal was found.");
    }

    private sealed class Candidate
    {
        public Candidate(SpoutFrameObservation frame)
        {
            Signal = frame.Signal;
            FirstReceivedAt = frame.ReceivedAt;
            LastReceivedAt = frame.ReceivedAt;
            LastFrameSequence = frame.FrameSequence;
            FrameCount = 1;
        }

        public StableVideoSignal Signal { get; }

        public Domain.Timing.MonotonicTimestamp FirstReceivedAt { get; }

        public Domain.Timing.MonotonicTimestamp LastReceivedAt { get; private set; }

        public ulong LastFrameSequence { get; private set; }

        public int FrameCount { get; private set; }

        public bool HasSameSignature(StableVideoSignal signal) =>
            string.Equals(Signal.SenderId, signal.SenderId, StringComparison.Ordinal) &&
            Signal.AdapterLuid == signal.AdapterLuid &&
            string.Equals(
                Signal.GpuIdentity,
                signal.GpuIdentity,
                StringComparison.Ordinal) &&
            Signal.GpuVendor == signal.GpuVendor &&
            Signal.Width == signal.Width &&
            Signal.Height == signal.Height &&
            Signal.PixelFormat == signal.PixelFormat;

        public void Observe(SpoutFrameObservation frame)
        {
            LastReceivedAt = frame.ReceivedAt;
            LastFrameSequence = frame.FrameSequence;
            FrameCount++;
        }
    }
}
