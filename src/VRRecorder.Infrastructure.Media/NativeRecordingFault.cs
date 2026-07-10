namespace VRRecorder.Infrastructure.Media;

public sealed record NativeRecordingFault(
    int Status,
    string Message);
