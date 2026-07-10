namespace VRRecorder.Application.Storage;

public sealed class RecordingFileFinalizationException : IOException
{
    public RecordingFileFinalizationException(
        string message,
        RecoverableRecording recoveryCandidate,
        Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(recoveryCandidate);
        RecoveryCandidate = recoveryCandidate;
    }

    public RecoverableRecording RecoveryCandidate { get; }
}
