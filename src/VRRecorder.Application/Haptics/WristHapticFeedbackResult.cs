namespace VRRecorder.Application.Haptics;

public abstract record WristHapticFeedbackResult
{
    private WristHapticFeedbackResult()
    {
    }

    public sealed record Delivered(long Revision)
        : WristHapticFeedbackResult;

    public sealed record Disabled(long Revision)
        : WristHapticFeedbackResult;

    public sealed record Ignored(long Revision)
        : WristHapticFeedbackResult;

    public sealed record Failed(long Revision, Exception Failure)
        : WristHapticFeedbackResult;
}
