namespace VRRecorder.DesignSystem;

public sealed record UiActionSnapshot(
    string SemanticId,
    UiCommandId Command,
    string IconSemanticId,
    UiComponentRole ComponentRole,
    UiColorRole ColorRole,
    bool IsEnabled,
    LocalizedText VisibleLabel,
    LocalizedText AccessibleName,
    LocalizedText Tooltip,
    int MinimumTargetDp);
