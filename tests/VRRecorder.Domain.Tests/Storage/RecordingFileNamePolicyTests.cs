using VRRecorder.Domain.Storage;

namespace VRRecorder.Domain.Tests.Storage;

public sealed class RecordingFileNamePolicyTests
{
    [Fact]
    public void FirstSegmentUsesCapturedLocalTimestampAndRecordingSuffix()
    {
        var timestamp = new RecordingSessionTimestamp(new DateTimeOffset(
            2026,
            7,
            10,
            12,
            34,
            56,
            TimeSpan.FromHours(9)));
        var descriptor = new RecordingFileDescriptor(
            timestamp,
            Width: 1920,
            Height: 1080,
            FramesPerSecond: 30,
            SegmentNumber: 1);

        var names = RecordingFileNamePolicy.Create(
            descriptor,
            collisionOrdinal: 1);

        Assert.Equal(
            "VR-Recorder_20260710_123456_1920x1080_30fps.mp4",
            names.FinalFileName);
        Assert.Equal(
            "VR-Recorder_20260710_123456_1920x1080_30fps.recording.mp4",
            names.TemporaryFileName);
        Assert.Equal(
            new DateTimeOffset(
                2026,
                7,
                10,
                3,
                34,
                56,
                TimeSpan.Zero),
            timestamp.UtcStartedAt);
    }

    [Fact]
    public void SegmentAndCollisionOrdinalsAreBothPreserved()
    {
        var descriptor = new RecordingFileDescriptor(
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            Width: 1080,
            Height: 1920,
            FramesPerSecond: 60,
            SegmentNumber: 2);

        var names = RecordingFileNamePolicy.Create(
            descriptor,
            collisionOrdinal: 2);

        Assert.Equal(
            "VR-Recorder_20260710_123456_1080x1920_60fps_part002_002.mp4",
            names.FinalFileName);
        Assert.Equal(
            "VR-Recorder_20260710_123456_1080x1920_60fps_part002_002.recording.mp4",
            names.TemporaryFileName);
    }
}
