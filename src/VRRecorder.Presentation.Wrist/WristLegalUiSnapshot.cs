using VRRecorder.Application.Compliance;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public enum WristLegalAction
{
    Back,
    OpenLicense,
    PreviousPage,
    NextPage,
}

public sealed record WristLegalComponentSnapshot(
    string Id,
    string DisplayName,
    string Version,
    string LicenseExpression,
    LocalizedText AccessibleName);

public sealed record WristLegalDetailFieldSnapshot(
    LocalizedText Label,
    string Value);

public sealed record WristLegalDocumentSnapshot(
    LegalDocumentReference Reference,
    LocalizedText KindLabel,
    string RelativePath,
    LocalizedText AccessibleName,
    int MinimumTargetDp);

public sealed record WristLegalTextPageSnapshot(
    string Text,
    int PageNumber,
    int PageCount,
    int FirstVisibleLine,
    int TotalLines,
    LocalizedText AccessiblePageLabel);

public sealed record WristLegalNavigationActionSnapshot(
    string SemanticId,
    WristLegalAction Action,
    bool IsEnabled,
    LocalizedText VisibleLabel,
    LocalizedText AccessibleName,
    LocalizedText Tooltip,
    int MinimumTargetDp);

public sealed record WristLegalUiSnapshot(
    long Revision,
    WristLegalView View,
    LocalizedText Title,
    LocalizedText VersionLabel,
    LocalizedText BundleIdentityLabel,
    LocalizedText ManifestSha256Label,
    IReadOnlyList<WristLegalComponentSnapshot> Components,
    IReadOnlyList<WristLegalDetailFieldSnapshot> DetailFields,
    IReadOnlyList<WristLegalDocumentSnapshot> Documents,
    LegalDocumentReference? SelectedDocument,
    WristLegalTextPageSnapshot? LicensePage,
    IReadOnlyList<WristLegalNavigationActionSnapshot> NavigationActions,
    IReadOnlyList<UiActionSnapshot> FixedRecordingActions,
    LocalizedText? StatusMessage)
{
    public WristLegalTextPageSnapshot? DocumentPage => LicensePage;
}
