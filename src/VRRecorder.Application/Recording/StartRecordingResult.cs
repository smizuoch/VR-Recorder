namespace VRRecorder.Application.Recording;

public abstract record StartRecordingResult
{
    private StartRecordingResult()
    {
    }

    public sealed record Started(RecordingHandle Handle) : StartRecordingResult;

    public sealed record NoSignal : StartRecordingResult;
}
