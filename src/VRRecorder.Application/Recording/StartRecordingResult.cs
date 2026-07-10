namespace VRRecorder.Application.Recording;

public abstract record StartRecordingResult
{
    private StartRecordingResult()
    {
    }

    public sealed record Started(
        RecordingHandle Handle,
        Task AutoStopCompletion) : StartRecordingResult;

    public sealed record NoSignal : StartRecordingResult;
}
