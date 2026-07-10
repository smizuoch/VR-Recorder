using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Desktop;

public sealed class RightsAcknowledgedDesktopRecordingStartRequestSource
    : IDesktopRecordingStartRequestSource
{
    private readonly IDesktopRecordingStartRequestSource _inner;
    private readonly RecordingRightsGate _rightsGate;

    public RightsAcknowledgedDesktopRecordingStartRequestSource(
        IDesktopRecordingStartRequestSource inner,
        RecordingRightsGate rightsGate)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(rightsGate);
        _inner = inner;
        _rightsGate = rightsGate;
    }

    public async Task<DesktopRecordingStartRequest> GetAsync(
        CancellationToken cancellationToken)
    {
        if (!await _rightsGate
                .IsAcknowledgedAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            throw new RecordingRightsAcknowledgementRequiredException(
                RecordingRightsNotice.CurrentVersion);
        }

        return await _inner
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
