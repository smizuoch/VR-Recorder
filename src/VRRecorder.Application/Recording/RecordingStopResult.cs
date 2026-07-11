using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Recording;

public sealed record RecordingStopResult(
    PendingRecording Recording,
    long VideoPacketCount,
    long AudioPacketCount,
    RecordingMediaExpectation? MediaExpectation = null,
    RecordingSessionStatistics? Statistics = null);
