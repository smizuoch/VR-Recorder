namespace VRRecorder.Application.Desktop;

public sealed class RecordingRightsAcknowledgementRequiredException
    : InvalidOperationException
{
    public RecordingRightsAcknowledgementRequiredException(int noticeVersion)
        : base(
            $"Recording rights notice version {noticeVersion} must be " +
            "acknowledged before recording can start.")
    {
        NoticeVersion = noticeVersion;
    }

    public int NoticeVersion { get; }
}
