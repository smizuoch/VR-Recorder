using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Presentation.Wrist;

public sealed record WristUiSnapshot(
    long Revision,
    RecorderState State,
    UiStateCue StateCue,
    WristPage Page,
    IReadOnlyList<UiActionSnapshot> Actions);
