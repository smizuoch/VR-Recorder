using VRRecorder.Domain.Recording;

namespace VRRecorder.Presentation.Wrist;

public enum WristTextureUpdateReason
{
    None,
    InitialFrame,
    RevisionChanged,
    PresentationChanged,
    RecordingHeartbeat,
}

public sealed record WristTextureUpdateCursor(
    long Revision,
    long PresentationRevision,
    RecorderState State,
    TimeSpan RenderedAt);

public sealed record WristTextureUpdateDecision(
    bool ShouldRender,
    WristTextureUpdateReason Reason,
    WristTextureUpdateCursor NextCursor);

public static class WristTextureUpdatePolicy
{
    public static readonly TimeSpan RecordingInterval =
        TimeSpan.FromMilliseconds(100);

    public static WristTextureUpdateDecision Evaluate(
        WristTextureUpdateCursor? previous,
        WristUiSnapshot snapshot,
        TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(snapshot.Revision);
        ArgumentOutOfRangeException.ThrowIfNegative(
            snapshot.PresentationRevision);
        if (now < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "The wrist update timestamp cannot be negative.");
        }

        if (previous is null)
        {
            return Render(
                snapshot,
                now,
                WristTextureUpdateReason.InitialFrame);
        }
        ValidatePrevious(previous, snapshot, now);
        if (snapshot.Revision > previous.Revision)
        {
            return Render(
                snapshot,
                now,
                WristTextureUpdateReason.RevisionChanged);
        }
        if (snapshot.PresentationRevision > previous.PresentationRevision)
        {
            return Render(
                snapshot,
                now,
                WristTextureUpdateReason.PresentationChanged);
        }

        var recordingLike = snapshot.State is
            RecorderState.Recording or RecorderState.SignalLost;
        if (recordingLike &&
            now - previous.RenderedAt >= RecordingInterval)
        {
            return Render(
                snapshot,
                now,
                WristTextureUpdateReason.RecordingHeartbeat);
        }

        return new WristTextureUpdateDecision(
            ShouldRender: false,
            WristTextureUpdateReason.None,
            previous);
    }

    private static WristTextureUpdateDecision Render(
        WristUiSnapshot snapshot,
        TimeSpan now,
        WristTextureUpdateReason reason) =>
        new(
            ShouldRender: true,
            reason,
            new WristTextureUpdateCursor(
                snapshot.Revision,
                snapshot.PresentationRevision,
                snapshot.State,
                now));

    private static void ValidatePrevious(
        WristTextureUpdateCursor previous,
        WristUiSnapshot snapshot,
        TimeSpan now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(previous.Revision);
        ArgumentOutOfRangeException.ThrowIfNegative(
            previous.PresentationRevision);
        if (!Enum.IsDefined(previous.State))
        {
            throw new ArgumentOutOfRangeException(
                nameof(previous),
                previous.State,
                "The previous wrist state is not defined.");
        }
        if (previous.RenderedAt < TimeSpan.Zero ||
            now < previous.RenderedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "Wrist update timestamps must be monotonic.");
        }
        if (snapshot.Revision < previous.Revision)
        {
            throw new InvalidOperationException(
                "A stale wrist snapshot cannot replace a rendered revision.");
        }
        if (snapshot.Revision == previous.Revision &&
            snapshot.PresentationRevision < previous.PresentationRevision)
        {
            throw new InvalidOperationException(
                "A stale wrist presentation cannot replace a rendered presentation.");
        }
        if (snapshot.Revision == previous.Revision &&
            snapshot.State != previous.State)
        {
            throw new InvalidOperationException(
                "A wrist state change must increment the snapshot revision.");
        }
    }
}
