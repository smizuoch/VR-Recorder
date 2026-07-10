namespace VRRecorder.Application.Storage;

public abstract record RecordingFinalizationResult
{
    private RecordingFinalizationResult()
    {
    }

    public sealed record Saved(FinalizedRecording Recording)
        : RecordingFinalizationResult;

    public sealed record RecoveryRequired(FinalizedRecording Recording)
        : RecordingFinalizationResult;
}
