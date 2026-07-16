using VRRecorder.Application.Presentation;

namespace VRRecorder.Presentation.Wrist;

public interface IWristUiSnapshotProjector
{
    WristUiSnapshot Project(RecorderStatusSnapshot status);
}
