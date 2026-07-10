using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public interface IUiLocalizer
{
    IReadOnlyCollection<string> ResourceKeys { get; }

    LocalizedText Resolve(string resourceKey);
}
