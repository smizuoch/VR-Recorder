using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristLegalCatalogV3ProjectionTests
{
    [Fact]
    public async Task ProjectsV3IdentityCopyrightAndEverySelectableDocument()
    {
        var catalog = Catalog();
        var reader = new TypedDocumentReader(catalog);
        var sink = new CapturingComplianceFaultSink();
        var controller = new WristLegalController(
            reader,
            sink,
            linesPerPage: 2);
        var projector = new WristLegalProjector(EnglishUiLocalizer.Instance);

        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("example", CancellationToken.None);

        Assert.Equal(catalog.BundleId, controller.State.BundleId);
        Assert.Equal(catalog.ManifestSha256, controller.State.ManifestSha256);
        Assert.Equal(
            "Copyright 2026 Example Authors",
            controller.State.SelectedComponent?.CopyrightNotice);
        var detail = projector.Project(controller.State, ReadyStatus());
        Assert.Equal(
            $"Legal Bundle identity {catalog.BundleId}",
            detail.BundleIdentityLabel.Value);
        Assert.Equal(
            $"Manifest SHA-256 {catalog.ManifestSha256}",
            detail.ManifestSha256Label.Value);
        Assert.Contains(detail.DetailFields, field =>
            field.Label.Value == "Copyright" &&
            field.Value == "Copyright 2026 Example Authors");
        Assert.Equal(
            catalog.Components[0].LegalDocuments,
            detail.Documents.Select(document => document.Reference));
        Assert.All(detail.Documents, document =>
            Assert.True(document.MinimumTargetDp >= 56));

        var attribution = catalog.Components[0].LegalDocuments.Single(
            reference => reference.Kind == LegalDocumentKind.Attribution);
        await controller.ShowDocumentAsync(
            attribution,
            CancellationToken.None);

        var document = projector.Project(controller.State, ReadyStatus());
        Assert.Equal(attribution, document.SelectedDocument);
        Assert.Equal(
            "authenticated Attribution document\n",
            document.DocumentPage?.Text);
        Assert.Empty(document.FixedRecordingActions);
        Assert.Equal(0, sink.CallCount);
    }

    [Fact]
    public async Task RejectedDocumentClearsAllV3ContentAndKeepsOneOperationStop()
    {
        var reader = new TypedDocumentReader(Catalog());
        var sink = new CapturingComplianceFaultSink();
        var controller = new WristLegalController(
            reader,
            sink,
            linesPerPage: 2);
        var projector = new WristLegalProjector(EnglishUiLocalizer.Instance);
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("example", CancellationToken.None);
        var notice = controller.State.SelectedComponent!.LegalDocuments.Single(
            reference => reference.Kind == LegalDocumentKind.Notice);
        await controller.ShowDocumentAsync(notice, CancellationToken.None);
        reader.RejectDocumentReads = true;

        await controller.RefreshAsync(CancellationToken.None);

        Assert.Equal(WristLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.BundleId);
        Assert.Null(controller.State.ProductVersion);
        Assert.Null(controller.State.ManifestSha256);
        Assert.Null(controller.State.SelectedComponent);
        Assert.Null(controller.State.SelectedDocument);
        Assert.Null(controller.State.FullDocumentText);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Empty(controller.State.Components);
        Assert.Equal(1, sink.CallCount);
        var unavailable = projector.Project(
            controller.State,
            new RecorderStatusSnapshot(
                Revision: 9,
                State: RecorderState.Recording,
                AvailableActions: RecorderAvailableActions.Stop));
        Assert.Null(unavailable.DocumentPage);
        var stop = Assert.Single(unavailable.FixedRecordingActions);
        Assert.Equal("recording.stop", stop.SemanticId);
        Assert.True(stop.IsEnabled);
    }

    private static LegalCatalogSnapshot Catalog() =>
        new(
            "https://example.invalid/spdx/wrist-v3",
            "0.3.0",
            new string('b', 64),
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

    private static RecorderStatusSnapshot ReadyStatus() =>
        new(
            Revision: 1,
            State: RecorderState.Ready,
            AvailableActions: RecorderAvailableActions.Start);

    private sealed class TypedDocumentReader(LegalCatalogSnapshot catalog)
        : ILegalCatalogReader
    {
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
}
