using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Storage;

public sealed class RecordingFileFinalizationUseCase
{
    private readonly IRecordingFileFinalizer _finalizer;
    private readonly IRecordingFileValidator _validator;
    private readonly IRecordingRecoveryStore _recovery;
    private readonly ISavedRecordingSink _savedRecordings;

    public RecordingFileFinalizationUseCase(
        IRecordingFileFinalizer finalizer,
        IRecordingFileValidator validator,
        IRecordingRecoveryStore recovery,
        ISavedRecordingSink savedRecordings)
    {
        ArgumentNullException.ThrowIfNull(finalizer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(savedRecordings);

        _finalizer = finalizer;
        _validator = validator;
        _recovery = recovery;
        _savedRecordings = savedRecordings;
    }

    public async Task<RecordingFinalizationResult> ExecuteAsync(
        PendingRecording recording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        var finalized = await _finalizer
            .FinalizeAsync(recording, cancellationToken)
            .ConfigureAwait(false);
        var validation = await _validator
            .ValidateAsync(finalized, cancellationToken)
            .ConfigureAwait(false);
        if (validation == RecordingFileValidation.Invalid)
        {
            await _recovery
                .QuarantineAsync(finalized, cancellationToken)
                .ConfigureAwait(false);
            return new RecordingFinalizationResult.RecoveryRequired(finalized);
        }

        await _savedRecordings
            .PublishAsync(finalized, cancellationToken)
            .ConfigureAwait(false);

        return new RecordingFinalizationResult.Saved(finalized);
    }
}
