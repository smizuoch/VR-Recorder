using VRRecorder.Application.Compliance;

namespace VRRecorder.Presentation.Wrist;

public sealed record WristLegalState(
    long Revision,
    WristLegalView View,
    string? ProductVersion,
    IReadOnlyList<LegalCatalogComponent> Components,
    LegalCatalogComponent? SelectedComponent,
    string? FullLicenseText,
    int FirstVisibleLine,
    int LinesPerPage,
    IReadOnlyList<LegalCatalogIssue> Issues);
