using VRRecorder.Application.Camera;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Recording;

public sealed record RecordingLifecycleStartResult(
    RecorderState State,
    VrChatCameraConnectionResolution Connection,
    StartRecordingResult? Recording,
    CameraSnapshotStartFailure? SnapshotFailure = null);
