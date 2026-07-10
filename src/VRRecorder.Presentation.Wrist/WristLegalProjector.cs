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
            components,
            detail,
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

    private IReadOnlyList<WristLegalDetailFieldSnapshot> ProjectDetail(
        LegalCatalogComponent component) =>
    [
        Field("legal.field.name", component.DisplayName),
        Field("legal.field.version", component.Version),
        Field("legal.field.license", component.LicenseExpression),
        Field("legal.field.usage", component.Usage),
        Field("legal.field.linkage", component.Linkage),
        Field(
            "legal.field.modified",
            _localizer.Resolve(component.Modified
                ? "legal.modified.yes"
                : "legal.modified.no").Value),
        Field("legal.field.source", component.SourceInformation),
    ];

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
