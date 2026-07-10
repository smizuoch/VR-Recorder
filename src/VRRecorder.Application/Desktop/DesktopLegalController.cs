using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopLegalController
{
    private readonly ILegalCatalogReader _reader;
    private readonly ILegalBundleFolderOpener _folderOpener;

    public DesktopLegalController(
        ILegalCatalogReader reader,
        ILegalBundleFolderOpener folderOpener)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(folderOpener);
        _reader = reader;
        _folderOpener = folderOpener;
        State = EmptyState(revision: 0, []);
    }

    public DesktopLegalState State { get; private set; }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        var catalog = await ReadCatalogAsync(cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null)
        {
            return;
        }

        State = new DesktopLegalState(
            State.Revision + 1,
            DesktopLegalView.ComponentList,
            catalog.BundleId,
            catalog.ProductVersion,
            SortComponents(catalog.Components),
            SelectedComponent: null,
            FullLicenseText: null,
            Issues: []);
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
            FailClosed(
            [
                new LegalCatalogIssue(
                    "legal-catalog-component-not-found",
                    componentId),
            ]);
            return;
        }

        State = new DesktopLegalState(
            State.Revision + 1,
            DesktopLegalView.ComponentDetail,
            catalog.BundleId,
            catalog.ProductVersion,
            SortComponents(catalog.Components),
            component,
            FullLicenseText: null,
            Issues: []);
    }

    public async Task ShowLicenseAsync(CancellationToken cancellationToken)
    {
        var selected = State.SelectedComponent ??
                       throw new InvalidOperationException(
                           "A component must be selected before its license is opened.");
        var result = await _reader
            .ReadLicenseTextAsync(selected.Id, cancellationToken)
            .ConfigureAwait(false);
        if (result is LegalTextReadResult.Rejected rejected)
        {
            FailClosed(rejected.Issues);
            return;
        }

        var document = ((LegalTextReadResult.Available)result).Document;
        if (!string.Equals(
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
                    selected.Id),
            ]);
            return;
        }

        State = State with
        {
            Revision = State.Revision + 1,
            View = DesktopLegalView.LicenseText,
            FullLicenseText = document.Text,
            Issues = [],
        };
    }

    public Task RefreshAsync(CancellationToken cancellationToken) =>
        State.View switch
        {
            DesktopLegalView.ComponentDetail when
                State.SelectedComponent is not null =>
                ShowDetailAsync(
                    State.SelectedComponent.Id,
                    cancellationToken),
            DesktopLegalView.LicenseText when
                State.SelectedComponent is not null =>
                ShowLicenseAsync(cancellationToken),
            _ => OpenAsync(cancellationToken),
        };

    public async Task OpenLicenseFolderAsync(
        CancellationToken cancellationToken)
    {
        var catalog = await ReadCatalogAsync(cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null)
        {
            return;
        }

        var result = await _folderOpener
            .OpenAsync(catalog.BundleId, cancellationToken)
            .ConfigureAwait(false);
        if (result is LegalFolderOpenResult.Rejected rejected)
        {
            FailClosed(rejected.Issues);
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

        FailClosed(((LegalCatalogReadResult.Rejected)result).Issues);
        return null;
    }

    private void FailClosed(IReadOnlyList<LegalCatalogIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        State = EmptyState(State.Revision + 1, issues.ToArray());
    }

    private static DesktopLegalState EmptyState(
        long revision,
        IReadOnlyList<LegalCatalogIssue> issues) =>
        new(
            revision,
            DesktopLegalView.Unavailable,
            BundleId: null,
            ProductVersion: null,
            Components: [],
            SelectedComponent: null,
            FullLicenseText: null,
            issues);

    private static LegalCatalogComponent[] SortComponents(
        IReadOnlyList<LegalCatalogComponent> components) =>
        components
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToArray();
}
