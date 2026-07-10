using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Compliance;

public sealed class RecordingRightsGate
{
    private readonly IRecordingRightsAcknowledgementStore _store;
    private readonly IWallClock _clock;

    public RecordingRightsGate(
        IRecordingRightsAcknowledgementStore store,
        IWallClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _clock = clock;
    }

    public async Task<bool> IsAcknowledgedAsync(
        CancellationToken cancellationToken)
    {
        var acknowledgement = await _store
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return acknowledgement is not null &&
               acknowledgement.NoticeVersion ==
               RecordingRightsNotice.CurrentVersion &&
               acknowledgement.AcknowledgedAtUtc <=
               _clock.LocalNow.ToUniversalTime();
    }

    public Task AcknowledgeAsync(CancellationToken cancellationToken) =>
        _store.SaveAsync(
            new RecordingRightsAcknowledgement(
                RecordingRightsNotice.CurrentVersion,
                _clock.LocalNow.ToUniversalTime()),
            cancellationToken);
}
