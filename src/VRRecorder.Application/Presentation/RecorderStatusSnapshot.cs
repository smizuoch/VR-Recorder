using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Presentation;

public sealed record RecorderStatusSnapshot(
    long Revision,
    RecorderState State,
    RecorderAvailableActions AvailableActions);
