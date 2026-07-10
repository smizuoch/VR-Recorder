using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopLegalCatalogV3ProjectionTests
{
    [Fact]
    public async Task ProjectsAuthenticatedV3MetadataAndBrowsesEveryTypedDocument()
    {
        var catalog = Catalog();
        var reader = new TypedDocumentReader(catalog);
        var controller = new DesktopLegalController(
            reader,
            new NeverOpenedFolderOpener(),
            new CapturingComplianceFaultSink());

        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("example", CancellationToken.None);

        Assert.Equal(catalog.ManifestSha256, controller.State.ManifestSha256);
        Assert.Equal(
            "Copyright 2026 Example Authors",
            controller.State.SelectedComponent?.CopyrightNotice);
        Assert.Equal(
            catalog.Components[0].LegalDocuments,
            controller.State.SelectedComponent?.LegalDocuments);

        foreach (var reference in catalog.Components[0].LegalDocuments)
        {
            await controller.ShowDocumentAsync(
                reference,
                CancellationToken.None);

            Assert.Equal(DesktopLegalView.LicenseText, controller.State.View);
            Assert.Equal(reference, controller.State.SelectedDocument);
            Assert.Equal(
                $"authenticated {reference.Kind} document\n",
                controller.State.FullDocumentText);
        }

        Assert.Equal(
            catalog.Components[0].LegalDocuments,
            reader.RequestedDocuments);
    }

    [Fact]
    public async Task RejectedTypedDocumentClearsAllV3ContentAndNotifiesGlobalFault()
    {
        var reader = new TypedDocumentReader(Catalog());
        var sink = new CapturingComplianceFaultSink();
        var controller = new DesktopLegalController(
            reader,
            new NeverOpenedFolderOpener(),
            sink);
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("example", CancellationToken.None);
        var notice = controller.State.SelectedComponent!.LegalDocuments.Single(
            reference => reference.Kind == LegalDocumentKind.Notice);
        await controller.ShowDocumentAsync(notice, CancellationToken.None);
        reader.RejectDocumentReads = true;

        await controller.RefreshAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.BundleId);
        Assert.Null(controller.State.ProductVersion);
        Assert.Null(controller.State.ManifestSha256);
        Assert.Null(controller.State.SelectedComponent);
        Assert.Null(controller.State.SelectedDocument);
        Assert.Null(controller.State.FullDocumentText);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Empty(controller.State.Components);
        Assert.Equal(1, sink.CallCount);
    }

    private static LegalCatalogSnapshot Catalog()
    {
        var manifestSha256 = new string('a', 64);
        return new LegalCatalogSnapshot(
            "https://example.invalid/spdx/desktop-v3",
            "0.3.0",
            manifestSha256,
            [new LegalCatalogComponent(
                "example",
                "Example",
                "1.2.3",
                "MIT",
                "runtime",
                "managed-library",
                false,
                "offline source@example",
                "Copyright 2026 Example Authors",
                Documents())]);
    }

    private static LegalDocumentReference[] Documents() =>
    [
        new(LegalDocumentKind.License, "LICENSES/example/LICENSE.txt"),
        new(LegalDocumentKind.Notice, "NOTICES/example/NOTICE.txt"),
        new(LegalDocumentKind.Copyright, "COPYRIGHTS/example.txt"),
        new(LegalDocumentKind.Attribution, "RIGHTS/example.txt"),
        new(
            LegalDocumentKind.AssetManifest,
            "MATERIAL-SYMBOLS-MANIFEST.json"),
    ];

    private sealed class TypedDocumentReader(LegalCatalogSnapshot catalog)
        : ILegalCatalogReader
    {
        public List<LegalDocumentReference> RequestedDocuments { get; } = [];

        public bool RejectDocumentReads { get; set; }

        public Task<LegalCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<LegalCatalogReadResult>(
                new LegalCatalogReadResult.Available(catalog));
        }

        public Task<LegalTextReadResult> ReadDocumentAsync(
            string componentId,
            LegalDocumentReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedDocuments.Add(reference);
            LegalTextReadResult result = RejectDocumentReads
                ? new LegalTextReadResult.Rejected(
                [
                    new LegalCatalogIssue(
                        "legal-bundle-payload-hash-mismatch",
                        reference.RelativePath),
                ])
                : new LegalTextReadResult.Available(new LegalTextDocument(
                    componentId,
                    reference,
                    $"authenticated {reference.Kind} document\n"));
            return Task.FromResult(result);
        }

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken)
        {
            var reference = catalog.Components.Single(component =>
                component.Id == componentId).LegalDocuments.Single(document =>
                document.Kind == LegalDocumentKind.License);
            return ReadDocumentAsync(
                componentId,
                reference,
                cancellationToken);
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

    private sealed class NeverOpenedFolderOpener : ILegalBundleFolderOpener
    {
        public Task<LegalFolderOpenResult> OpenAsync(
            string expectedBundleId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Folder {expectedBundleId} must not be opened by this test.");
    }
}
