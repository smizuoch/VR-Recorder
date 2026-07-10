using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Presentation.Wrist;

public sealed class WristLegalController
{
    private readonly ILegalCatalogReader _reader;
    private readonly IComplianceFaultSink _complianceFaultSink;
    private readonly int _linesPerPage;

    public WristLegalController(
        ILegalCatalogReader reader,
        IComplianceFaultSink complianceFaultSink,
        int linesPerPage = 20)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(complianceFaultSink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(linesPerPage);
        _reader = reader;
        _complianceFaultSink = complianceFaultSink;
        _linesPerPage = linesPerPage;
        State = EmptyState(WristLegalView.Unavailable, revision: 0, []);
    }

    public WristLegalState State { get; private set; }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        var catalog = await ReadCatalogAsync(cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null)
        {
            return;
        }

        State = CreateCatalogState(
            catalog,
            WristLegalView.ComponentList,
            selectedComponent: null);
    }

    public async Task ShowDetailAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        var catalog = await ReadCatalogAsync(cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null)
        {
            return;
        }

        var component = catalog.Components.SingleOrDefault(item =>
            string.Equals(item.Id, componentId, StringComparison.Ordinal));
        if (component is null)
        {
            await FailClosedAsync(
            [
                new LegalCatalogIssue(
                    "legal-catalog-component-not-found",
                    componentId),
            ]).ConfigureAwait(false);
            return;
        }

        State = CreateCatalogState(
            catalog,
            WristLegalView.ComponentDetail,
            component);
    }

    public async Task ShowLicenseAsync(CancellationToken cancellationToken)
    {
        var selected = State.SelectedComponent ??
                       throw new InvalidOperationException(
                           "A component must be selected before its license is opened.");
        var reference = selected.LegalDocuments.Single(document =>
            document.Kind == LegalDocumentKind.License);
        await RefreshDocumentAsync(
                reference,
                firstVisibleLine: 0,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task ShowDocumentAsync(
        LegalDocumentReference reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return RefreshDocumentAsync(
            reference,
            firstVisibleLine: 0,
            cancellationToken);
    }

    public Task NextPageAsync(CancellationToken cancellationToken) =>
        RefreshCurrentDocumentAsync(
            State.FirstVisibleLine + _linesPerPage,
            cancellationToken);

    public Task PreviousPageAsync(CancellationToken cancellationToken) =>
        RefreshCurrentDocumentAsync(
            State.FirstVisibleLine - _linesPerPage,
            cancellationToken);

    public Task ScrollAsync(
        int lineDelta,
        CancellationToken cancellationToken) =>
        RefreshCurrentDocumentAsync(
            checked(State.FirstVisibleLine + lineDelta),
            cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken) =>
        State.View switch
        {
            WristLegalView.ComponentDetail when
                State.SelectedComponent is not null =>
                ShowDetailAsync(
                    State.SelectedComponent.Id,
                    cancellationToken),
            WristLegalView.LicenseText => RefreshCurrentDocumentAsync(
                State.FirstVisibleLine,
                cancellationToken),
            _ => OpenAsync(cancellationToken),
        };

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
                SelectedDocument = null,
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
                SelectedDocument = null,
            };
        }
    }

    private Task RefreshCurrentDocumentAsync(
        int requestedFirstVisibleLine,
        CancellationToken cancellationToken)
    {
        if (State.View != WristLegalView.LicenseText ||
            State.SelectedComponent is null ||
            State.SelectedDocument is null)
        {
            throw new InvalidOperationException(
                "Legal document navigation requires an open document.");
        }

        return RefreshDocumentAsync(
            State.SelectedDocument,
            requestedFirstVisibleLine,
            cancellationToken);
    }

    private async Task RefreshDocumentAsync(
        LegalDocumentReference reference,
        int firstVisibleLine,
        CancellationToken cancellationToken)
    {
        var selected = State.SelectedComponent ??
                       throw new InvalidOperationException(
                           "A component must be selected before its legal document is opened.");
        var expectedReference = selected.LegalDocuments.SingleOrDefault(
            document => document == reference);
        if (expectedReference is null)
        {
            await FailClosedAsync(
            [
                new LegalCatalogIssue(
                    "legal-catalog-document-reference-mismatch",
                    selected.Id),
            ]).ConfigureAwait(false);
            return;
        }

        var result = await _reader
            .ReadDocumentAsync(
                selected.Id,
                expectedReference,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is LegalTextReadResult.Rejected rejected)
        {
            await FailClosedAsync(rejected.Issues).ConfigureAwait(false);
            return;
        }

        var document = ((LegalTextReadResult.Available)result).Document;
        if (!string.Equals(
                selected.Id,
                document.ComponentId,
                StringComparison.Ordinal) ||
            document.Reference != expectedReference)
        {
            await FailClosedAsync(
            [
                new LegalCatalogIssue(
                    "legal-catalog-document-identity-mismatch",
                    selected.Id),
            ]).ConfigureAwait(false);
            return;
        }

        var lineCount = WristLegalTextLines.Split(document.Text).Count;
        var maximumFirstLine = Math.Max(0, lineCount - _linesPerPage);
        State = State with
        {
            Revision = State.Revision + 1,
            View = WristLegalView.LicenseText,
            FullLicenseText = document.Text,
            SelectedDocument = expectedReference,
            FirstVisibleLine = Math.Clamp(
                firstVisibleLine,
                0,
                maximumFirstLine),
            Issues = [],
        };
    }

    private async Task FailClosedAsync(
        IReadOnlyList<LegalCatalogIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        State = EmptyState(
            WristLegalView.Unavailable,
            State.Revision + 1,
            issues.ToArray());
        try
        {
            await _complianceFaultSink
                .EnterComplianceFaultAsync()
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Local legal data remains cleared even when the global notifier fails.
        }
    }

    private async Task<LegalCatalogSnapshot?> ReadCatalogAsync(
        CancellationToken cancellationToken)
    {
        var result = await _reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result is LegalCatalogReadResult.Available available)
        {
            return available.Catalog;
        }

        await FailClosedAsync(
                ((LegalCatalogReadResult.Rejected)result).Issues)
            .ConfigureAwait(false);
        return null;
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
            issues,
            BundleId: null,
            ManifestSha256: null,
            SelectedDocument: null);

    private WristLegalState CreateCatalogState(
        LegalCatalogSnapshot catalog,
        WristLegalView view,
        LegalCatalogComponent? selectedComponent) =>
        new(
            State.Revision + 1,
            view,
            catalog.ProductVersion,
            SortComponents(catalog.Components),
            selectedComponent,
            FullLicenseText: null,
            FirstVisibleLine: 0,
            _linesPerPage,
            Issues: [],
            catalog.BundleId,
            catalog.ManifestSha256,
            SelectedDocument: null);

    private static LegalCatalogComponent[] SortComponents(
        IReadOnlyList<LegalCatalogComponent> components) =>
        components
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToArray();

}
