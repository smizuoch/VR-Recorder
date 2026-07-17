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
        var controller = new DesktopLegalController(
            reader,
            folderOpener,
            new CapturingComplianceFaultSink());

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
            new CapturingLegalBundleFolderOpener(),
            new CapturingComplianceFaultSink());
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
            new CapturingLegalBundleFolderOpener(reject: true),
            new CapturingComplianceFaultSink());
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

    [Fact]
    public async Task ConcurrentLicenseReadCannotMixTextWithLaterSelection()
    {
        var reader = new StubLegalCatalogReader(Catalog());
        var controller = new DesktopLegalController(
            reader,
            new CapturingLegalBundleFolderOpener(),
            new CapturingComplianceFaultSink());
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("a", CancellationToken.None);
        reader.HoldLicenseReads();

        var showLicense = controller.ShowLicenseAsync(CancellationToken.None);
        await reader.WaitUntilLicenseReadRequestedAsync();
        var showLaterDetail = controller.ShowDetailAsync(
            "b",
            CancellationToken.None);
        reader.ReleaseLicenseReads();

        await Task.WhenAll(showLicense, showLaterDetail);

        Assert.Equal(DesktopLegalView.ComponentDetail, controller.State.View);
        Assert.Equal("b", controller.State.SelectedComponent?.Id);
        Assert.Null(controller.State.FullLicenseText);
    }

    [Fact]
    public async Task SinkFailureCannotPreservePreviouslyAuthenticatedText()
    {
        var reader = new StubLegalCatalogReader(Catalog());
        var sink = new CapturingComplianceFaultSink(reject: true);
        var controller = new DesktopLegalController(
            reader,
            new CapturingLegalBundleFolderOpener(),
            sink);
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("a", CancellationToken.None);
        await controller.ShowLicenseAsync(CancellationToken.None);
        reader.RejectLicenseReads = true;

        await controller.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, sink.CallCount);
        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.BundleId);
        Assert.Null(controller.State.ProductVersion);
        Assert.Null(controller.State.SelectedComponent);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Empty(controller.State.Components);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("detail")]
    [InlineData("folder")]
    public async Task RejectedCatalogFailsClosedForEveryCatalogEntryPoint(
        string operation)
    {
        var reader = new StubLegalCatalogReader(Catalog())
        {
            RejectCatalogReads = true,
        };
        var sink = new CapturingComplianceFaultSink();
        var controller = new DesktopLegalController(
            reader,
            new CapturingLegalBundleFolderOpener(),
            sink);

        await (operation switch
        {
            "open" => controller.OpenAsync(CancellationToken.None),
            "detail" => controller.ShowDetailAsync(
                "a",
                CancellationToken.None),
            "folder" => controller.OpenLicenseFolderAsync(
                CancellationToken.None),
            _ => throw new InvalidOperationException(operation),
        });

        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-catalog-rejected");
        Assert.Equal(1, sink.CallCount);
    }

    [Fact]
    public async Task UnknownComponentFailsClosedWithoutKeepingCatalog()
    {
        var sink = new CapturingComplianceFaultSink();
        var controller = new DesktopLegalController(
            new StubLegalCatalogReader(Catalog()),
            new CapturingLegalBundleFolderOpener(),
            sink);

        await controller.ShowDetailAsync("missing", CancellationToken.None);

        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Empty(controller.State.Components);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-catalog-component-not-found" &&
            issue.Subject == "missing");
        Assert.Equal(1, sink.CallCount);
    }

    [Fact]
    public async Task ForeignDocumentReferenceFailsClosedBeforeReadingText()
    {
        var reader = new StubLegalCatalogReader(Catalog());
        var controller = new DesktopLegalController(
            reader,
            new CapturingLegalBundleFolderOpener(),
            new CapturingComplianceFaultSink());
        await controller.ShowDetailAsync("a", CancellationToken.None);

        await controller.ShowDocumentAsync(
            new LegalDocumentReference(
                LegalDocumentKind.Notice,
                "LICENSES/a/NOTICE.txt"),
            CancellationToken.None);

        Assert.Equal(0, reader.LicenseReadCount);
        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-catalog-document-reference-mismatch");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MismatchedDocumentIdentityFailsClosed(
        bool mismatchComponent,
        bool mismatchReference)
    {
        var reader = new StubLegalCatalogReader(Catalog())
        {
            MismatchDocumentComponent = mismatchComponent,
            MismatchDocumentReference = mismatchReference,
        };
        var controller = new DesktopLegalController(
            reader,
            new CapturingLegalBundleFolderOpener(),
            new CapturingComplianceFaultSink());
        await controller.ShowDetailAsync("a", CancellationToken.None);

        await controller.ShowLicenseAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-catalog-document-identity-mismatch");
    }

    [Fact]
    public async Task FailedCallerCannotPoisonLaterLegalNavigation()
    {
        var controller = new DesktopLegalController(
            new StubLegalCatalogReader(Catalog()),
            new CapturingLegalBundleFolderOpener(),
            new CapturingComplianceFaultSink());

        var failed = controller.ShowLicenseAsync(CancellationToken.None);
        var later = controller.OpenAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        await later;
        Assert.Equal(DesktopLegalView.ComponentList, controller.State.View);
        Assert.Equal(["a", "b"], controller.State.Components.Select(item => item.Id));
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
        private TaskCompletionSource? _licenseReadRelease;
        private readonly TaskCompletionSource _licenseReadRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool RejectCatalogReads { get; set; }

        public bool RejectLicenseReads { get; set; }

        public bool MismatchDocumentComponent { get; set; }

        public bool MismatchDocumentReference { get; set; }

        public int LicenseReadCount { get; private set; }

        public void HoldLicenseReads() =>
            _licenseReadRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilLicenseReadRequestedAsync() =>
            _licenseReadRequested.Task;

        public void ReleaseLicenseReads() =>
            _licenseReadRelease?.TrySetResult();

        public Task<LegalCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RejectCatalogReads)
            {
                return Task.FromResult<LegalCatalogReadResult>(
                    new LegalCatalogReadResult.Rejected(
                    [
                        new LegalCatalogIssue(
                            "legal-catalog-rejected",
                            "bundle-desktop"),
                    ]));
            }

            return Task.FromResult<LegalCatalogReadResult>(
                new LegalCatalogReadResult.Available(catalog));
        }

        public async Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LicenseReadCount++;
            _licenseReadRequested.TrySetResult();
            if (_licenseReadRelease is not null)
            {
                await _licenseReadRelease.Task.WaitAsync(cancellationToken);
            }

            if (RejectLicenseReads)
            {
                return new LegalTextReadResult.Rejected(
                [
                    new LegalCatalogIssue(
                        "legal-bundle-payload-hash-mismatch",
                        $"LICENSES/{componentId}/LICENSE.txt"),
                ]);
            }

            var reference = MismatchDocumentReference
                ? new LegalDocumentReference(
                    LegalDocumentKind.Notice,
                    $"LICENSES/{componentId}/NOTICE.txt")
                : new LegalDocumentReference(
                    LegalDocumentKind.License,
                    $"LICENSES/{componentId}/LICENSE.txt");
            return new LegalTextReadResult.Available(new LegalTextDocument(
                MismatchDocumentComponent ? "other" : componentId,
                reference,
                $"{componentId} LICENSE\nline two\n"));
        }
    }

    private sealed class CapturingComplianceFaultSink(bool reject = false)
        : IComplianceFaultSink
    {
        public int CallCount { get; private set; }

        public ValueTask EnterComplianceFaultAsync()
        {
            CallCount++;
            return reject
                ? ValueTask.FromException(
                    new InvalidOperationException("sink unavailable"))
                : ValueTask.CompletedTask;
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
