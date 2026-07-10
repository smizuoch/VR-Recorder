using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristLegalController
{
    private readonly ILegalCatalogReader _reader;
    private readonly int _linesPerPage;

    public WristLegalController(
        ILegalCatalogReader reader,
        int linesPerPage = 20)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(linesPerPage);
        _reader = reader;
        _linesPerPage = linesPerPage;
        State = EmptyState(WristLegalView.Unavailable, revision: 0, []);
    }

    public WristLegalState State { get; private set; }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        var result = await _reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result is LegalCatalogReadResult.Rejected rejected)
        {
            FailClosed(rejected.Issues);
            return;
        }

        var catalog = ((LegalCatalogReadResult.Available)result).Catalog;
        State = new WristLegalState(
            State.Revision + 1,
            WristLegalView.ComponentList,
            catalog.ProductVersion,
            SortComponents(catalog.Components),
            SelectedComponent: null,
            FullLicenseText: null,
            FirstVisibleLine: 0,
            _linesPerPage,
            Issues: []);
    }

    public async Task ShowDetailAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        var result = await _reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result is LegalCatalogReadResult.Rejected rejected)
        {
            FailClosed(rejected.Issues);
            return;
        }

        var catalog = ((LegalCatalogReadResult.Available)result).Catalog;
        var component = catalog.Components.SingleOrDefault(item =>
            string.Equals(item.Id, componentId, StringComparison.Ordinal));
        if (component is null)
        {
            FailClosed(
            [
                new LegalCatalogIssue(
                    "legal-catalog-component-not-found",
                    componentId),
            ]);
            return;
        }

        State = new WristLegalState(
            State.Revision + 1,
            WristLegalView.ComponentDetail,
            catalog.ProductVersion,
            SortComponents(catalog.Components),
            component,
            FullLicenseText: null,
            FirstVisibleLine: 0,
            _linesPerPage,
            Issues: []);
    }

    public async Task ShowLicenseAsync(CancellationToken cancellationToken)
    {
        var selected = State.SelectedComponent ??
                       throw new InvalidOperationException(
                           "A component must be selected before its license is opened.");
        await RefreshLicenseAsync(
                selected.Id,
                firstVisibleLine: 0,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task NextPageAsync(CancellationToken cancellationToken) =>
        RefreshCurrentLicenseAsync(
            State.FirstVisibleLine + _linesPerPage,
            cancellationToken);

    public Task PreviousPageAsync(CancellationToken cancellationToken) =>
        RefreshCurrentLicenseAsync(
            State.FirstVisibleLine - _linesPerPage,
            cancellationToken);

    public Task ScrollAsync(
        int lineDelta,
        CancellationToken cancellationToken) =>
        RefreshCurrentLicenseAsync(
            checked(State.FirstVisibleLine + lineDelta),
            cancellationToken);

    public void Back()
    {
        if (State.View == WristLegalView.LicenseText &&
            State.SelectedComponent is not null)
        {
            State = State with
            {
                Revision = State.Revision + 1,
                View = WristLegalView.ComponentDetail,
                FullLicenseText = null,
                FirstVisibleLine = 0,
            };
        }
        else if (State.View == WristLegalView.ComponentDetail)
        {
            State = State with
            {
                Revision = State.Revision + 1,
                View = WristLegalView.ComponentList,
                SelectedComponent = null,
                FullLicenseText = null,
                FirstVisibleLine = 0,
            };
        }
    }

    private Task RefreshCurrentLicenseAsync(
        int requestedFirstVisibleLine,
        CancellationToken cancellationToken)
    {
        if (State.View != WristLegalView.LicenseText ||
            State.SelectedComponent is null)
        {
            throw new InvalidOperationException(
                "License navigation requires an open license document.");
        }

        return RefreshLicenseAsync(
            State.SelectedComponent.Id,
            requestedFirstVisibleLine,
            cancellationToken);
    }

    private async Task RefreshLicenseAsync(
        string componentId,
        int firstVisibleLine,
        CancellationToken cancellationToken)
    {
        var result = await _reader
            .ReadLicenseTextAsync(componentId, cancellationToken)
            .ConfigureAwait(false);
        if (result is LegalTextReadResult.Rejected rejected)
        {
            FailClosed(rejected.Issues);
            return;
        }

        var document = ((LegalTextReadResult.Available)result).Document;
        var selected = State.SelectedComponent;
        if (selected is null ||
            !string.Equals(
                selected.Id,
                document.ComponentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                selected.LicenseTextPath,
                document.RelativePath,
                StringComparison.Ordinal))
        {
            FailClosed(
            [
                new LegalCatalogIssue(
                    "legal-catalog-license-identity-mismatch",
                    componentId),
            ]);
            return;
        }

        var lineCount = WristLegalTextLines.Split(document.Text).Count;
        var maximumFirstLine = Math.Max(0, lineCount - _linesPerPage);
        State = State with
        {
            Revision = State.Revision + 1,
            View = WristLegalView.LicenseText,
            FullLicenseText = document.Text,
            FirstVisibleLine = Math.Clamp(
                firstVisibleLine,
                0,
                maximumFirstLine),
            Issues = [],
        };
    }

    private void FailClosed(IReadOnlyList<LegalCatalogIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        State = EmptyState(
            WristLegalView.Unavailable,
            State.Revision + 1,
            issues.ToArray());
    }

    private WristLegalState EmptyState(
        WristLegalView view,
        long revision,
        IReadOnlyList<LegalCatalogIssue> issues) =>
        new(
            revision,
            view,
            ProductVersion: null,
            Components: [],
            SelectedComponent: null,
            FullLicenseText: null,
            FirstVisibleLine: 0,
            _linesPerPage,
            issues);

    private static LegalCatalogComponent[] SortComponents(
        IReadOnlyList<LegalCatalogComponent> components) =>
        components
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToArray();
}
