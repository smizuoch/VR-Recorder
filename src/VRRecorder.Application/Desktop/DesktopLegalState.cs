using VRRecorder.Application.Compliance;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopLegalState(
    long Revision,
    DesktopLegalView View,
    string? BundleId,
    string? ProductVersion,
    IReadOnlyList<LegalCatalogComponent> Components,
    LegalCatalogComponent? SelectedComponent,
    string? FullLicenseText,
    IReadOnlyList<LegalCatalogIssue> Issues,
    string? ManifestSha256 = null,
    LegalDocumentReference? SelectedDocument = null)
{
    public string? FullDocumentText => FullLicenseText;
}
