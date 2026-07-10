using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristLegalControllerTests
{
    [Fact]
    public async Task NavigatesAuthenticatedListDetailAndPagedLicenseText()
    {
        var reader = new StubLegalCatalogReader(Catalog());
        var controller = new WristLegalController(
            reader,
            new CapturingComplianceFaultSink(),
            linesPerPage: 2);

        await controller.OpenAsync(CancellationToken.None);

        Assert.Equal(WristLegalView.ComponentList, controller.State.View);
        Assert.Equal("0.1.0", controller.State.ProductVersion);
        Assert.Equal(["a", "b"], controller.State.Components.Select(item => item.Id));

        await controller.ShowDetailAsync("a", CancellationToken.None);

        Assert.Equal(WristLegalView.ComponentDetail, controller.State.View);
        Assert.Equal("a", controller.State.SelectedComponent?.Id);

        await controller.ShowLicenseAsync(CancellationToken.None);

        Assert.Equal(WristLegalView.LicenseText, controller.State.View);
        Assert.Equal("line 1\nline 2\nline 3\nline 4\n", controller.State.FullLicenseText);
        Assert.Equal(0, controller.State.FirstVisibleLine);

        await controller.NextPageAsync(CancellationToken.None);

        Assert.Equal(2, controller.State.FirstVisibleLine);

        await controller.ScrollAsync(-1, CancellationToken.None);

        Assert.Equal(1, controller.State.FirstVisibleLine);

        await controller.PreviousPageAsync(CancellationToken.None);

        Assert.Equal(0, controller.State.FirstVisibleLine);
        Assert.True(reader.CatalogReadCount >= 2);
        Assert.Equal(4, reader.LicenseReadCount);
    }

    [Fact]
    public async Task RejectedRefreshClearsPreviouslyDisplayedLicenseText()
    {
        var reader = new StubLegalCatalogReader(Catalog());
        var sink = new CapturingComplianceFaultSink();
        var controller = new WristLegalController(
            reader,
            sink,
            linesPerPage: 2);
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("a", CancellationToken.None);
        await controller.ShowLicenseAsync(CancellationToken.None);
        reader.RejectLicenseReads = true;

        await controller.NextPageAsync(CancellationToken.None);

        Assert.Equal(WristLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Null(controller.State.SelectedComponent);
        Assert.Empty(controller.State.Components);
        Assert.Equal(0, controller.State.FirstVisibleLine);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-bundle-payload-hash-mismatch");
        Assert.Equal(1, sink.CallCount);
    }

    [Fact]
    public async Task BackFromLicenseDiscardsFullTextBeforeShowingDetail()
    {
        var controller = new WristLegalController(
            new StubLegalCatalogReader(Catalog()),
            new CapturingComplianceFaultSink(),
            linesPerPage: 2);
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("a", CancellationToken.None);
        await controller.ShowLicenseAsync(CancellationToken.None);

        controller.Back();

        Assert.Equal(WristLegalView.ComponentDetail, controller.State.View);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Equal("a", controller.State.SelectedComponent?.Id);

        controller.Back();

        Assert.Equal(WristLegalView.ComponentList, controller.State.View);
        Assert.Null(controller.State.SelectedComponent);
    }

    private static LegalCatalogSnapshot Catalog() =>
        new(
            "https://example.invalid/spdx/wrist-legal",
            "0.1.0",
            [Component("a"), Component("b")]);

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

        public int CatalogReadCount { get; private set; }

        public int LicenseReadCount { get; private set; }

        public Task<LegalCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CatalogReadCount++;
            return Task.FromResult<LegalCatalogReadResult>(
                new LegalCatalogReadResult.Available(catalog));
        }

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LicenseReadCount++;
            LegalTextReadResult result = RejectLicenseReads
                ? new LegalTextReadResult.Rejected(
                    [
                        new LegalCatalogIssue(
                            "legal-bundle-payload-hash-mismatch",
                            $"LICENSES/{componentId}/LICENSE.txt"),
                    ])
                : new LegalTextReadResult.Available(new LegalTextDocument(
                    componentId,
                    $"LICENSES/{componentId}/LICENSE.txt",
                    "line 1\nline 2\nline 3\nline 4\n"));
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingComplianceFaultSink : IComplianceFaultSink
    {
        public int CallCount { get; private set; }

        public ValueTask EnterComplianceFaultAsync()
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
