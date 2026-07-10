using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopLegalController
{
    private readonly ILegalCatalogReader _reader;
    private readonly ILegalBundleFolderOpener _folderOpener;
    private readonly IComplianceFaultSink _complianceFaultSink;
    private readonly object _operationGate = new();
    private Task _operationTail = Task.CompletedTask;

    public DesktopLegalController(
        ILegalCatalogReader reader,
        ILegalBundleFolderOpener folderOpener,
        IComplianceFaultSink complianceFaultSink)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(folderOpener);
        ArgumentNullException.ThrowIfNull(complianceFaultSink);
        _reader = reader;
        _folderOpener = folderOpener;
        _complianceFaultSink = complianceFaultSink;
        State = EmptyState(revision: 0, []);
    }

    public DesktopLegalState State { get; private set; }

    public Task OpenAsync(CancellationToken cancellationToken) =>
        ExecuteSerializedAsync(OpenCoreAsync, cancellationToken);

    public Task ShowDetailAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        return ExecuteSerializedAsync(
            token => ShowDetailCoreAsync(componentId, token),
            cancellationToken);
    }

    public Task ShowLicenseAsync(CancellationToken cancellationToken) =>
        ExecuteSerializedAsync(ShowLicenseCoreAsync, cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken) =>
        ExecuteSerializedAsync(RefreshCoreAsync, cancellationToken);

    public Task OpenLicenseFolderAsync(
        CancellationToken cancellationToken) =>
        ExecuteSerializedAsync(
            OpenLicenseFolderCoreAsync,
            cancellationToken);

    private async Task OpenCoreAsync(CancellationToken cancellationToken)
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

    private async Task ShowDetailCoreAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
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

    private async Task ShowLicenseCoreAsync(
        CancellationToken cancellationToken)
    {
        var selected = State.SelectedComponent ??
                       throw new InvalidOperationException(
                           "A component must be selected before its license is opened.");
        var result = await _reader
            .ReadLicenseTextAsync(selected.Id, cancellationToken)
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
            !string.Equals(
                selected.LicenseTextPath,
                document.RelativePath,
                StringComparison.Ordinal))
        {
            await FailClosedAsync(
            [
                new LegalCatalogIssue(
                    "legal-catalog-license-identity-mismatch",
                    selected.Id),
            ]).ConfigureAwait(false);
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

    private Task RefreshCoreAsync(CancellationToken cancellationToken) =>
        State.View switch
        {
            DesktopLegalView.ComponentDetail when
                State.SelectedComponent is not null =>
                ShowDetailCoreAsync(
                    State.SelectedComponent.Id,
                    cancellationToken),
            DesktopLegalView.LicenseText when
                State.SelectedComponent is not null =>
                ShowLicenseCoreAsync(cancellationToken),
            _ => OpenCoreAsync(cancellationToken),
        };

    private async Task OpenLicenseFolderCoreAsync(
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
            await FailClosedAsync(rejected.Issues).ConfigureAwait(false);
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

    private async Task FailClosedAsync(
        IReadOnlyList<LegalCatalogIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        State = EmptyState(State.Revision + 1, issues.ToArray());
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

    private Task ExecuteSerializedAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        lock (_operationGate)
        {
            var queued = ExecuteAfterAsync(
                _operationTail,
                operation,
                cancellationToken);
            _operationTail = queued;
            return queued;
        }
    }

    private static async Task ExecuteAfterAsync(
        Task preceding,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await preceding.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failed caller cannot poison later legal-view operations.
        }

        cancellationToken.ThrowIfCancellationRequested();
        await operation(cancellationToken).ConfigureAwait(false);
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
