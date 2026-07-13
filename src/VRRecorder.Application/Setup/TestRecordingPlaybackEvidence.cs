namespace VRRecorder.Application.Setup;

public sealed record TestRecordingPlaybackEvidence(
    TimeSpan Duration,
    bool IsFinalized,
    bool HasVideoStream,
    bool HasAudioStream,
    bool PlaybackStarted);
