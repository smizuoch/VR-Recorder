using System.Globalization;

namespace VRRecorder.Domain.Storage;

public static class RecordingFileNamePolicy
{
    public static RecordingFileNames Create(
        RecordingFileDescriptor descriptor,
        int collisionOrdinal)
    {
        var timestamp = descriptor.Timestamp.LocalStartedAt.ToString(
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture);
        var stem = string.Create(
            CultureInfo.InvariantCulture,
            $"VR-Recorder_{timestamp}_{descriptor.Width}x{descriptor.Height}_{descriptor.FramesPerSecond}fps");

        if (descriptor.SegmentNumber > 1)
        {
            stem = string.Create(
                CultureInfo.InvariantCulture,
                $"{stem}_part{descriptor.SegmentNumber:000}");
        }

        if (collisionOrdinal > 1)
        {
            stem = string.Create(
                CultureInfo.InvariantCulture,
                $"{stem}_{collisionOrdinal:000}");
        }

        return new RecordingFileNames(
            $"{stem}.mp4",
            $"{stem}.recording.mp4");
    }
}
