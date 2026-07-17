using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristTextureUpdatePolicyTests
{
    [Fact]
    public void UpdatesImmediatelyForFirstFrameAndEveryNewRevision()
    {
        var ready = Snapshot(1, RecorderState.Ready);

        var first = WristTextureUpdatePolicy.Evaluate(
            previous: null,
            ready,
            TimeSpan.Zero);
        var unchanged = WristTextureUpdatePolicy.Evaluate(
            first.NextCursor,
            ready,
            TimeSpan.FromSeconds(1));
        var changed = WristTextureUpdatePolicy.Evaluate(
            first.NextCursor,
            Snapshot(2, RecorderState.Ready),
            TimeSpan.FromMilliseconds(1));

        Assert.True(first.ShouldRender);
        Assert.Equal(WristTextureUpdateReason.InitialFrame, first.Reason);
        Assert.False(unchanged.ShouldRender);
        Assert.Equal(WristTextureUpdateReason.None, unchanged.Reason);
        Assert.True(changed.ShouldRender);
        Assert.Equal(WristTextureUpdateReason.RevisionChanged, changed.Reason);
    }

    [Fact]
    public void RecordingHeartbeatIsCappedAtTenHertzFromCommittedCursor()
    {
        var recording = Snapshot(5, RecorderState.Recording);
        var initial = WristTextureUpdatePolicy.Evaluate(
            previous: null,
            recording,
            TimeSpan.Zero);

        var tooEarly = WristTextureUpdatePolicy.Evaluate(
            initial.NextCursor,
            recording,
            TimeSpan.FromMilliseconds(99));
        var due = WristTextureUpdatePolicy.Evaluate(
            initial.NextCursor,
            recording,
            TimeSpan.FromMilliseconds(100));
        var committedTooEarly = WristTextureUpdatePolicy.Evaluate(
            due.NextCursor,
            recording,
            TimeSpan.FromMilliseconds(199));
        var committedDue = WristTextureUpdatePolicy.Evaluate(
            due.NextCursor,
            recording,
            TimeSpan.FromMilliseconds(200));

        Assert.False(tooEarly.ShouldRender);
        Assert.True(due.ShouldRender);
        Assert.Equal(
            WristTextureUpdateReason.RecordingHeartbeat,
            due.Reason);
        Assert.False(committedTooEarly.ShouldRender);
        Assert.True(committedDue.ShouldRender);
    }

    [Fact]
    public void CallerCanRetryWhenFailedRenderDoesNotCommitNextCursor()
    {
        var recording = Snapshot(7, RecorderState.Recording);
        var committed = WristTextureUpdatePolicy.Evaluate(
            previous: null,
            recording,
            TimeSpan.Zero).NextCursor;
        var failedAttempt = WristTextureUpdatePolicy.Evaluate(
            committed,
            recording,
            TimeSpan.FromMilliseconds(100));

        var retry = WristTextureUpdatePolicy.Evaluate(
            committed,
            recording,
            TimeSpan.FromMilliseconds(101));

        Assert.True(failedAttempt.ShouldRender);
        Assert.True(retry.ShouldRender);
        Assert.Equal(
            WristTextureUpdateReason.RecordingHeartbeat,
            retry.Reason);
    }

    [Fact]
    public void PresentationNavigationRendersWithoutARecorderRevisionChange()
    {
        var initial = Snapshot(8, RecorderState.Ready);
        var committed = WristTextureUpdatePolicy.Evaluate(
            previous: null,
            initial,
            TimeSpan.Zero).NextCursor;
        var navigated = initial with
        {
            Page = WristPage.Positioning,
            PresentationRevision = 1,
        };

        var decision = WristTextureUpdatePolicy.Evaluate(
            committed,
            navigated,
            TimeSpan.FromMilliseconds(1));

        Assert.True(decision.ShouldRender);
        Assert.Equal(
            WristTextureUpdateReason.PresentationChanged,
            decision.Reason);
    }

    [Theory]
    [InlineData(RecorderState.Recording)]
    [InlineData(RecorderState.SignalLost)]
    public void RecordingLikeStatesRefreshTelemetry(RecorderState state)
    {
        var snapshot = Snapshot(9, state);
        var committed = WristTextureUpdatePolicy.Evaluate(
            previous: null,
            snapshot,
            TimeSpan.Zero).NextCursor;

        var decision = WristTextureUpdatePolicy.Evaluate(
            committed,
            snapshot,
            TimeSpan.FromMilliseconds(100));

        Assert.True(decision.ShouldRender);
    }

    [Fact]
    public void RejectsInvalidAndNonMonotonicUpdateCursors()
    {
        var ready = Snapshot(10, RecorderState.Ready);
        var valid = new WristTextureUpdateCursor(
            10,
            2,
            RecorderState.Ready,
            TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristTextureUpdatePolicy.Evaluate(
                previous: null,
                ready,
                TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristTextureUpdatePolicy.Evaluate(
                valid with { State = (RecorderState)int.MaxValue },
                ready with { PresentationRevision = 2 },
                TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristTextureUpdatePolicy.Evaluate(
                valid with { RenderedAt = TimeSpan.FromTicks(-1) },
                ready with { PresentationRevision = 2 },
                TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristTextureUpdatePolicy.Evaluate(
                valid,
                ready with { PresentationRevision = 2 },
                TimeSpan.FromMilliseconds(999)));
        Assert.Throws<InvalidOperationException>(() =>
            WristTextureUpdatePolicy.Evaluate(
                valid,
                ready with
                {
                    Revision = 9,
                    PresentationRevision = 2,
                },
                TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidOperationException>(() =>
            WristTextureUpdatePolicy.Evaluate(
                valid,
                ready with { PresentationRevision = 1 },
                TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidOperationException>(() =>
            WristTextureUpdatePolicy.Evaluate(
                valid,
                ready with
                {
                    PresentationRevision = 2,
                    State = RecorderState.NoSignal,
                },
                TimeSpan.FromSeconds(1)));
    }

    private static WristUiSnapshot Snapshot(
        long revision,
        RecorderState state) =>
        new WristUiProjector(EnglishUiLocalizer.Instance).Project(
            new RecorderStatusSnapshot(
                revision,
                state,
                RecorderAvailableActions.None));
}
