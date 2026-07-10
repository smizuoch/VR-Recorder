using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public interface IUiLocalizer
{
    LocalizedText Resolve(string resourceKey);
}
