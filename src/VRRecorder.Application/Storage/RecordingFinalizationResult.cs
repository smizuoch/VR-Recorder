namespace VRRecorder.Application.Storage;

public abstract record RecordingFinalizationResult
{
    private RecordingFinalizationResult()
    {
    }

    public sealed record Saved(FinalizedRecording Recording)
        : RecordingFinalizationResult;

    public sealed record RecoveryRequired(
        RecordingRecoveryReason Reason,
        QuarantinedRecording Quarantine)
        : RecordingFinalizationResult;
}
