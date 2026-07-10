using VRRecorder.Domain.Video;

namespace VRRecorder.Domain.Storage;

public sealed record RecordingFileDescriptor
{
    public RecordingFileDescriptor(
        RecordingSessionTimestamp Timestamp,
        int Width,
        int Height,
        FrameRate FrameRate,
        int SegmentNumber)
    {
        this.Timestamp = Timestamp;
        this.Width = Width;
        this.Height = Height;
        this.FrameRate = FrameRate;
        this.SegmentNumber = SegmentNumber;
    }

    public RecordingSessionTimestamp Timestamp { get; }

    public int Width { get; }

    public int Height { get; }

    public FrameRate FrameRate { get; }

    public int SegmentNumber { get; }
}
