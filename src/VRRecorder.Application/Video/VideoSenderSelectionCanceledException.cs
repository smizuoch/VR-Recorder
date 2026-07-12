namespace VRRecorder.Application.Video;

public sealed class VideoSenderSelectionCanceledException
    : OperationCanceledException
{
    public VideoSenderSelectionCanceledException()
        : base("Video sender selection was canceled by the user.")
    {
    }
}
