namespace VRRecorder.Domain.Storage;

public sealed record RecordingFileNames(
    string FinalFileName,
    string TemporaryFileName);
