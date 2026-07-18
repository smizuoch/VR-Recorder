namespace VRRecorder.Infrastructure.Media;

public enum NativeRecordingFaultSource
{
    Unknown = 0,
    VideoEncoder = 1,
}

public sealed record NativeRecordingFault(
    int Status,
    string Message,
    NativeRecordingFaultSource Source = NativeRecordingFaultSource.Unknown);
