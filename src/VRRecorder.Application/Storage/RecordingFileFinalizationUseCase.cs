using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Storage;

public sealed class RecordingFileFinalizationUseCase
{
    private readonly IRecordingFileFinalizer _finalizer;
    private readonly ISavedRecordingSink _savedRecordings;

    public RecordingFileFinalizationUseCase(
        IRecordingFileFinalizer finalizer,
        ISavedRecordingSink savedRecordings)
    {
        ArgumentNullException.ThrowIfNull(finalizer);
        ArgumentNullException.ThrowIfNull(savedRecordings);

        _finalizer = finalizer;
        _savedRecordings = savedRecordings;
    }

    public async Task<FinalizedRecording> ExecuteAsync(
        PendingRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        var finalized = await _finalizer
            .FinalizeAsync(recording, cancellationToken)
            .ConfigureAwait(false);
        await _savedRecordings
            .PublishAsync(finalized, cancellationToken)
            .ConfigureAwait(false);

        return finalized;
    }
}
