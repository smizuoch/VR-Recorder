using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopLegalControllerTests
{
    [Fact]
    public async Task BrowsesDeterministicCatalogAndOpensAuthenticatedFolder()
    {
        var reader = new StubLegalCatalogReader(Catalog());
        var folderOpener = new CapturingLegalBundleFolderOpener();
        var controller = new DesktopLegalController(reader, folderOpener);

        await controller.OpenAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.ComponentList, controller.State.View);
        Assert.Equal("bundle-desktop", controller.State.BundleId);
        Assert.Equal("0.1.0", controller.State.ProductVersion);
        Assert.Equal(["a", "b"], controller.State.Components.Select(item => item.Id));

        await controller.ShowDetailAsync("a", CancellationToken.None);

        Assert.Equal(DesktopLegalView.ComponentDetail, controller.State.View);
        Assert.Equal("a", controller.State.SelectedComponent?.Id);

        await controller.ShowLicenseAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.LicenseText, controller.State.View);
        Assert.Equal("a LICENSE\nline two\n", controller.State.FullLicenseText);

        await controller.OpenLicenseFolderAsync(CancellationToken.None);

        Assert.Equal(["bundle-desktop"], folderOpener.ExpectedBundleIds);
        Assert.Equal(DesktopLegalView.LicenseText, controller.State.View);
        Assert.Equal("a LICENSE\nline two\n", controller.State.FullLicenseText);
    }

    [Fact]
    public async Task RejectedRefreshClearsEveryPreviouslyDisplayedValue()
    {
        var reader = new StubLegalCatalogReader(Catalog());
        var controller = new DesktopLegalController(
            reader,
            new CapturingLegalBundleFolderOpener());
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("a", CancellationToken.None);
        await controller.ShowLicenseAsync(CancellationToken.None);
        reader.RejectLicenseReads = true;

        await controller.RefreshAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.BundleId);
        Assert.Null(controller.State.ProductVersion);
        Assert.Null(controller.State.SelectedComponent);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Empty(controller.State.Components);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-bundle-payload-hash-mismatch");
    }

    [Fact]
    public async Task RejectedFolderAuthenticationFailsClosedWithoutStaleText()
    {
        var controller = new DesktopLegalController(
            new StubLegalCatalogReader(Catalog()),
            new CapturingLegalBundleFolderOpener(reject: true));
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("a", CancellationToken.None);
        await controller.ShowLicenseAsync(CancellationToken.None);

        await controller.OpenLicenseFolderAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Empty(controller.State.Components);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-folder-outside-install-bundle");
    }

    private static LegalCatalogSnapshot Catalog() =>
        new("bundle-desktop", "0.1.0", [Component("b"), Component("a")]);

    private static LegalCatalogComponent Component(string id) =>
        new(
            id,
            $"Component {id}",
            $"{id}.0.0",
            "MIT",
            "runtime-feature",
            "managed-library",
            id == "b",
            $"offline source {id}@commit",
            $"LICENSES/{id}/LICENSE.txt");

    private sealed class StubLegalCatalogReader(LegalCatalogSnapshot catalog)
        : ILegalCatalogReader
    {
        public bool RejectLicenseReads { get; set; }

        public Task<LegalCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<LegalCatalogReadResult>(
                new LegalCatalogReadResult.Available(catalog));
        }

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<LegalTextReadResult>(RejectLicenseReads
                ? new LegalTextReadResult.Rejected(
                [
                    new LegalCatalogIssue(
                        "legal-bundle-payload-hash-mismatch",
                        $"LICENSES/{componentId}/LICENSE.txt"),
                ])
                : new LegalTextReadResult.Available(new LegalTextDocument(
                    componentId,
                    $"LICENSES/{componentId}/LICENSE.txt",
                    $"{componentId} LICENSE\nline two\n")));
        }
    }

    private sealed class CapturingLegalBundleFolderOpener(bool reject = false)
        : ILegalBundleFolderOpener
    {
        public List<string> ExpectedBundleIds { get; } = [];

        public Task<LegalFolderOpenResult> OpenAsync(
            string expectedBundleId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpectedBundleIds.Add(expectedBundleId);
            return Task.FromResult<LegalFolderOpenResult>(reject
                ? new LegalFolderOpenResult.Rejected(
                [
                    new LegalCatalogIssue(
                        "legal-folder-outside-install-bundle",
                        "outside"),
                ])
                : new LegalFolderOpenResult.Opened("/install/legal"));
        }
    }
}
