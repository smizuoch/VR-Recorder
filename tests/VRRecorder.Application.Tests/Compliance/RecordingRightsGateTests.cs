using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.Compliance;

public sealed class RecordingRightsGateTests
{
    [Fact]
    public async Task MissingOrOutdatedAcknowledgementDoesNotAuthorizeRecording()
    {
        var store = new StubAcknowledgementStore();
        var gate = new RecordingRightsGate(
            store,
            new FixedWallClock(DateTimeOffset.UnixEpoch));

        Assert.False(await gate.IsAcknowledgedAsync(CancellationToken.None));

        store.Acknowledgement = new RecordingRightsAcknowledgement(
            RecordingRightsNotice.CurrentVersion - 1,
            DateTimeOffset.UnixEpoch);
        Assert.False(await gate.IsAcknowledgedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CurrentAcknowledgementAuthorizesRecording()
    {
        var store = new StubAcknowledgementStore
        {
            Acknowledgement = new RecordingRightsAcknowledgement(
                RecordingRightsNotice.CurrentVersion,
                new DateTimeOffset(
                    2026,
                    7,
                    10,
                    12,
                    34,
                    56,
                    TimeSpan.Zero)),
        };
        var gate = new RecordingRightsGate(
            store,
            new FixedWallClock(DateTimeOffset.UnixEpoch));

        Assert.True(await gate.IsAcknowledgedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AcknowledgePersistsCurrentNoticeAndUtcEvidence()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            11,
            14,
            15,
            16,
            TimeSpan.FromHours(9));
        var store = new StubAcknowledgementStore();
        var gate = new RecordingRightsGate(
            store,
            new FixedWallClock(now));

        await gate.AcknowledgeAsync(CancellationToken.None);

        Assert.Equal(
            new RecordingRightsAcknowledgement(
                RecordingRightsNotice.CurrentVersion,
                now.ToUniversalTime()),
            store.Acknowledgement);
        Assert.Equal(1, store.SaveCallCount);
    }

    private sealed class StubAcknowledgementStore
        : IRecordingRightsAcknowledgementStore
    {
        public RecordingRightsAcknowledgement? Acknowledgement { get; set; }

        public int SaveCallCount { get; private set; }

        public Task<RecordingRightsAcknowledgement?> LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Acknowledgement);
        }

        public Task SaveAsync(
            RecordingRightsAcknowledgement acknowledgement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            Acknowledgement = acknowledgement;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedWallClock(DateTimeOffset localNow) : IWallClock
    {
        public DateTimeOffset LocalNow { get; } = localNow;
    }
}
