using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public sealed class CompositeSavedRecordingSink : ISavedRecordingSink
{
    private readonly ISavedRecordingSink _first;
    private readonly ISavedRecordingSink _second;

    public CompositeSavedRecordingSink(
        ISavedRecordingSink first,
        ISavedRecordingSink second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        _first = first;
        _second = second;
    }

    public async Task PublishAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);
        cancellationToken.ThrowIfCancellationRequested();
        await _first
            .PublishAsync(recording, cancellationToken)
            .ConfigureAwait(false);
        await _second
            .PublishAsync(recording, cancellationToken)
            .ConfigureAwait(false);
    }
}
