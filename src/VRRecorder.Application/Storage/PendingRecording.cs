namespace VRRecorder.Application.Storage;

public sealed record PendingRecording(
    string TemporaryPath,
    string FinalPath);
