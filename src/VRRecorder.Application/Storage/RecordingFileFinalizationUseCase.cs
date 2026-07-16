using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Storage;

public sealed class RecordingFileFinalizationUseCase
{
    private readonly IRecordingFileFinalizer _finalizer;
    private readonly IRecordingFileValidator _validator;
    private readonly IRecordingRecoveryStore _recovery;
    private readonly ISavedRecordingSink _savedRecordings;
    private readonly IRecordingFinalizationEventSink? _events;

    public RecordingFileFinalizationUseCase(
        IRecordingFileFinalizer finalizer,
        IRecordingFileValidator validator,
        IRecordingRecoveryStore recovery,
        ISavedRecordingSink savedRecordings)
        : this(
            finalizer,
            validator,
            recovery,
            savedRecordings,
            events: null)
    {
    }

    public RecordingFileFinalizationUseCase(
        IRecordingFileFinalizer finalizer,
        IRecordingFileValidator validator,
        IRecordingRecoveryStore recovery,
        ISavedRecordingSink savedRecordings,
        IRecordingFinalizationEventSink? events)
    {
        ArgumentNullException.ThrowIfNull(finalizer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(savedRecordings);

        _finalizer = finalizer;
        _validator = validator;
        _recovery = recovery;
        _savedRecordings = savedRecordings;
        _events = events;
    }

    public async Task<RecordingFinalizationResult> ExecuteAsync(
        PendingRecording recording,
        CancellationToken cancellationToken) =>
        await ExecuteCoreAsync(
                recording,
                mediaExpectation: null,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<RecordingFinalizationResult> ExecuteAsync(
        RecordingStopResult stopped,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopped);
        return await ExecuteCoreAsync(
                stopped.Recording,
                stopped.MediaExpectation,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RecordingFinalizationResult> ExecuteCoreAsync(
        PendingRecording recording,
        RecordingMediaExpectation? mediaExpectation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        var pending = new FinalizedRecording(recording.TemporaryPath);
        var validation = mediaExpectation is null
            ? await _validator
                .ValidateAsync(pending, cancellationToken)
                .ConfigureAwait(false)
            : await _validator
                .ValidateAsync(
                    pending,
                    mediaExpectation,
                    cancellationToken)
                .ConfigureAwait(false);
        if (validation == RecordingFileValidation.Invalid)
        {
            var quarantined = await _recovery
                .QuarantineAsync(
                    new RecoverableRecording(recording.TemporaryPath),
                    cancellationToken)
                .ConfigureAwait(false);
            PublishRecoveryBestEffort(
                RecordingRecoveryReason.ValidationFailed);
            return new RecordingFinalizationResult.RecoveryRequired(
                RecordingRecoveryReason.ValidationFailed,
                quarantined);
        }

        FinalizedRecording finalized;
        try
        {
            finalized = await _finalizer
                .FinalizeAsync(recording, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RecordingFileFinalizationException exception)
        {
            var quarantined = await _recovery
                .QuarantineAsync(
                    exception.RecoveryCandidate,
                    cancellationToken)
                .ConfigureAwait(false);
            PublishRecoveryBestEffort(
                RecordingRecoveryReason.FinalizationFailed);
            return new RecordingFinalizationResult.RecoveryRequired(
                RecordingRecoveryReason.FinalizationFailed,
                quarantined);
        }

        await _savedRecordings
            .PublishAsync(finalized, cancellationToken)
            .ConfigureAwait(false);

        return new RecordingFinalizationResult.Saved(finalized);
    }

    private void PublishRecoveryBestEffort(RecordingRecoveryReason reason)
    {
        try
        {
            _events?.Publish(reason);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Recording finalization diagnostics failed: {0}",
                exception.GetType().Name);
        }
    }
}
