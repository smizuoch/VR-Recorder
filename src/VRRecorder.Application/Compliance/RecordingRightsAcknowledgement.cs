namespace VRRecorder.Application.Compliance;

public sealed record RecordingRightsAcknowledgement
{
    public RecordingRightsAcknowledgement(
        int noticeVersion,
        DateTimeOffset acknowledgedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(noticeVersion);
        if (acknowledgedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Recording-rights acknowledgement evidence must use UTC.",
                nameof(acknowledgedAtUtc));
        }

        NoticeVersion = noticeVersion;
        AcknowledgedAtUtc = acknowledgedAtUtc;
    }

    public int NoticeVersion { get; }

    public DateTimeOffset AcknowledgedAtUtc { get; }
}
