using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class RightsAcknowledgedDesktopRecordingStartRequestSourceTests
{
    [Fact]
    public async Task MissingAcknowledgementBlocksBeforeCreatingStartRequest()
    {
        var acknowledgements = new StubAcknowledgementStore();
        var inner = new CapturingStartRequestSource();
        var source = new RightsAcknowledgedDesktopRecordingStartRequestSource(
            inner,
            new RecordingRightsGate(
                acknowledgements,
                new FixedWallClock(DateTimeOffset.UnixEpoch)));

        var failure = await Assert.ThrowsAsync<
            RecordingRightsAcknowledgementRequiredException>(() =>
            source.GetAsync(CancellationToken.None));

        Assert.Equal(RecordingRightsNotice.CurrentVersion, failure.NoticeVersion);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task CurrentAcknowledgementAllowsExactInnerRequest()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            11,
            1,
            2,
            3,
            TimeSpan.Zero);
        var acknowledgements = new StubAcknowledgementStore
        {
            Value = new RecordingRightsAcknowledgement(
                RecordingRightsNotice.CurrentVersion,
                now),
        };
        var inner = new CapturingStartRequestSource();
        var source = new RightsAcknowledgedDesktopRecordingStartRequestSource(
            inner,
            new RecordingRightsGate(
                acknowledgements,
                new FixedWallClock(now)));

        var request = await source.GetAsync(CancellationToken.None);

        Assert.Same(inner.Request, request);
        Assert.Equal(1, inner.CallCount);
    }

    private sealed class CapturingStartRequestSource
        : IDesktopRecordingStartRequestSource
    {
        public DesktopRecordingStartRequest Request { get; } = new(
            selectedServiceId: null,
            new StartRecordingCommand(
                SelfTimer.FromSeconds(0),
                RecordingDuration.Infinite,
                new OutputPath(Path.GetTempPath()),
                new FrameRate(30)));

        public int CallCount { get; private set; }

        public Task<DesktopRecordingStartRequest> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Request);
        }
    }

    private sealed class StubAcknowledgementStore
        : IRecordingRightsAcknowledgementStore
    {
        public RecordingRightsAcknowledgement? Value { get; init; }

        public Task<RecordingRightsAcknowledgement?> LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Value);
        }

        public Task SaveAsync(
            RecordingRightsAcknowledgement acknowledgement,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Save was not expected.");
    }

    private sealed class FixedWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = localNow;
    }
}
