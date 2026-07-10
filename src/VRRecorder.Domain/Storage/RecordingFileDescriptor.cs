namespace VRRecorder.Domain.Storage;

public sealed record RecordingFileDescriptor
{
    public RecordingFileDescriptor(
        RecordingSessionTimestamp Timestamp,
        int Width,
        int Height,
        int FramesPerSecond,
        int SegmentNumber)
    {
        this.Timestamp = Timestamp;
        this.Width = Width;
        this.Height = Height;
        this.FramesPerSecond = FramesPerSecond;
        this.SegmentNumber = SegmentNumber;
    }

    public RecordingSessionTimestamp Timestamp { get; }

    public int Width { get; }

    public int Height { get; }

    public int FramesPerSecond { get; }

    public int SegmentNumber { get; }
}
