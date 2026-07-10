using System.Globalization;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristLegalProjector
{
    private readonly IUiLocalizer _localizer;
    private readonly WristUiProjector _recorderProjector;

    public WristLegalProjector(IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        _recorderProjector = new WristUiProjector(localizer);
    }

    public WristLegalUiSnapshot Project(
        WristLegalState state,
        RecorderStatusSnapshot recorderStatus)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(recorderStatus);
        var components = state.View == WristLegalView.ComponentList
            ? state.Components
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(ProjectComponent)
                .ToArray()
            : [];
        var detail = state.View == WristLegalView.ComponentDetail &&
                     state.SelectedComponent is not null
            ? ProjectDetail(state.SelectedComponent)
            : [];
        var documents = state.View == WristLegalView.ComponentDetail &&
                        state.SelectedComponent is not null
            ? state.SelectedComponent.LegalDocuments
                .OrderBy(document => document.Kind)
                .ThenBy(
                    document => document.RelativePath,
                    StringComparer.Ordinal)
                .Select(ProjectDocument)
                .ToArray()
            : [];
        var page = state.View == WristLegalView.LicenseText
            ? ProjectLicensePage(state)
            : null;
        var fixedRecordingActions = _recorderProjector
            .Project(recorderStatus, WristPage.Legal)
            .Actions
            .Where(action => string.Equals(
                action.SemanticId,
                "recording.stop",
                StringComparison.Ordinal))
            .ToArray();

        return new WristLegalUiSnapshot(
            state.Revision,
            state.View,
            _localizer.Resolve("legal.title"),
            state.ProductVersion is null
                ? new LocalizedText("legal.version.format", string.Empty)
                : Format("legal.version.format", state.ProductVersion),
            state.BundleId is null
                ? new LocalizedText("legal.bundle.format", string.Empty)
                : Format("legal.bundle.format", state.BundleId),
            state.ManifestSha256 is null
                ? new LocalizedText("legal.manifest.format", string.Empty)
                : Format("legal.manifest.format", state.ManifestSha256),
            components,
            detail,
            documents,
            state.SelectedDocument,
            page,
            ProjectNavigation(state, page),
            fixedRecordingActions,
            state.View == WristLegalView.Unavailable
                ? _localizer.Resolve("legal.unavailable.label")
                : null);
    }

    private WristLegalComponentSnapshot ProjectComponent(
        LegalCatalogComponent component) =>
        new(
            component.Id,
            component.DisplayName,
            component.Version,
            component.LicenseExpression,
            Format(
                "legal.component.accessible.format",
                component.DisplayName,
                component.Version,
                component.LicenseExpression));

    private List<WristLegalDetailFieldSnapshot> ProjectDetail(
        LegalCatalogComponent component)
    {
        var fields = new List<WristLegalDetailFieldSnapshot>
        {
            Field("legal.field.name", component.DisplayName),
            Field("legal.field.version", component.Version),
            Field("legal.field.license", component.LicenseExpression),
        };
        if (!string.IsNullOrWhiteSpace(component.CopyrightNotice))
        {
            fields.Add(Field(
                "legal.field.copyright",
                component.CopyrightNotice));
        }

        fields.Add(Field("legal.field.usage", component.Usage));
        fields.Add(Field("legal.field.linkage", component.Linkage));
        fields.Add(Field(
            "legal.field.modified",
            _localizer.Resolve(component.Modified
                ? "legal.modified.yes"
                : "legal.modified.no").Value));
        fields.Add(Field("legal.field.source", component.SourceInformation));
        return fields;
    }

    private WristLegalDocumentSnapshot ProjectDocument(
        LegalDocumentReference reference)
    {
        var kind = _localizer.Resolve(reference.Kind switch
        {
            LegalDocumentKind.License => "legal.document.license",
            LegalDocumentKind.Notice => "legal.document.notice",
            LegalDocumentKind.Copyright => "legal.document.copyright",
            LegalDocumentKind.Attribution => "legal.document.attribution",
            LegalDocumentKind.AssetManifest =>
                "legal.document.asset-manifest",
            _ => throw new ArgumentOutOfRangeException(
                nameof(reference),
                reference.Kind,
                "Unsupported legal document kind."),
        });
        return new WristLegalDocumentSnapshot(
            reference,
            kind,
            reference.RelativePath,
            Format(
                "legal.document.accessible.format",
                kind.Value,
                reference.RelativePath),
            MinimumTargetDp: 56);
    }

    private WristLegalTextPageSnapshot? ProjectLicensePage(
        WristLegalState state)
    {
        if (state.FullLicenseText is null)
        {
            return null;
        }

        var lines = WristLegalTextLines.Split(state.FullLicenseText);
        var totalLines = lines.Count;
        var pageCount = Math.Max(
            1,
            (int)Math.Ceiling(totalLines / (double)state.LinesPerPage));
        var firstLine = Math.Clamp(
            state.FirstVisibleLine,
            0,
            Math.Max(0, totalLines - state.LinesPerPage));
        var pageNumber = Math.Min(
            pageCount,
            firstLine / state.LinesPerPage + 1);
        var text = string.Join(
            '\n',
            lines.Skip(firstLine).Take(state.LinesPerPage));
        return new WristLegalTextPageSnapshot(
            text,
            pageNumber,
            pageCount,
            firstLine,
            totalLines,
            Format("legal.page.format", pageNumber, pageCount));
    }

    private IReadOnlyList<WristLegalNavigationActionSnapshot>
        ProjectNavigation(
            WristLegalState state,
            WristLegalTextPageSnapshot? page) =>
        state.View switch
        {
            WristLegalView.ComponentDetail =>
            [
                Action(
                    "legal.back",
                    WristLegalAction.Back,
                    "legal.back.short",
                    "legal.back.accessible"),
                Action(
                    "legal.open-license",
                    WristLegalAction.OpenLicense,
                    "legal.open-license.short",
                    "legal.open-license.accessible"),
            ],
            WristLegalView.LicenseText when page is not null =>
            [
                Action(
                    "legal.back",
                    WristLegalAction.Back,
                    "legal.back.short",
                    "legal.back.accessible"),
                Action(
                    "legal.previous-page",
                    WristLegalAction.PreviousPage,
                    "legal.previous-page.short",
                    "legal.previous-page.accessible",
                    page.FirstVisibleLine > 0),
                Action(
                    "legal.next-page",
                    WristLegalAction.NextPage,
                    "legal.next-page.short",
                    "legal.next-page.accessible",
                    page.FirstVisibleLine + state.LinesPerPage <
                    page.TotalLines),
            ],
            _ => [],
        };

    private WristLegalDetailFieldSnapshot Field(
        string resourceKey,
        string value) =>
        new(_localizer.Resolve(resourceKey), value);

    private WristLegalNavigationActionSnapshot Action(
        string semanticId,
        WristLegalAction action,
        string shortLabelKey,
        string accessibleNameKey,
        bool isEnabled = true)
    {
        var accessibleName = _localizer.Resolve(accessibleNameKey);
        return new WristLegalNavigationActionSnapshot(
            semanticId,
            action,
            isEnabled,
            _localizer.Resolve(shortLabelKey),
            accessibleName,
            accessibleName,
            MinimumTargetDp: 56);
    }

    private LocalizedText Format(string resourceKey, params object[] values)
    {
        var template = _localizer.Resolve(resourceKey);
        return new LocalizedText(
            resourceKey,
            string.Format(CultureInfo.CurrentCulture, template.Value, values));
    }
}
