using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class OfflineLegalAccessFirstRunSetupProbeTests
{
    [Fact]
    public async Task EveryAuthenticatedDocumentMustBeReadableWithExactIdentity()
    {
        var license = new LegalDocumentReference(
            LegalDocumentKind.License,
            "LICENSES/MIT.txt");
        var notice = new LegalDocumentReference(
            LegalDocumentKind.Notice,
            "NOTICES/component.txt");
        var component = Component("component", [license, notice]);
        var reader = new StubReader(
            new LegalCatalogReadResult.Available(Catalog([component])),
            new Dictionary<LegalDocumentReference, LegalTextReadResult>
            {
                [license] = Available(component.Id, license, "MIT License\n"),
                [notice] = Available(component.Id, notice, "Notice text\n"),
            });
        var probe = new OfflineLegalAccessFirstRunSetupProbe(reader);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.OfflineLegalAccess,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal([license, notice], reader.ReadReferences);
    }

    [Fact]
    public async Task EmptyCatalogCannotClaimOfflineLegalAccess()
    {
        var probe = new OfflineLegalAccessFirstRunSetupProbe(new StubReader(
            new LegalCatalogReadResult.Available(Catalog([])),
            new Dictionary<LegalDocumentReference, LegalTextReadResult>()));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.OfflineLegalAccess,
            CancellationToken.None));
    }

    [Fact]
    public async Task EmptyOrMismatchedDocumentCannotClaimOfflineAccess()
    {
        var reference = new LegalDocumentReference(
            LegalDocumentKind.License,
            "LICENSES/MIT.txt");
        var component = Component("component", [reference]);
        var reader = new StubReader(
            new LegalCatalogReadResult.Available(Catalog([component])),
            new Dictionary<LegalDocumentReference, LegalTextReadResult>
            {
                [reference] = Available("other-component", reference, string.Empty),
            });
        var probe = new OfflineLegalAccessFirstRunSetupProbe(reader);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.OfflineLegalAccess,
            CancellationToken.None));
    }

    [Fact]
    public async Task WrongStepRejectedCatalogAndDocumentFailuresDoNotVerify()
    {
        var reference = new LegalDocumentReference(
            LegalDocumentKind.License,
            "LICENSES/MIT.txt");
        var component = Component("component", [reference]);
        var validTexts = new Dictionary<
            LegalDocumentReference,
            LegalTextReadResult>
        {
            [reference] = Available(component.Id, reference, "MIT License"),
        };
        var validReader = new StubReader(
            new LegalCatalogReadResult.Available(Catalog([component])),
            validTexts);

        Assert.Throws<ArgumentNullException>(() =>
            new OfflineLegalAccessFirstRunSetupProbe(null!));
        Assert.False(await new OfflineLegalAccessFirstRunSetupProbe(validReader)
            .VerifyAsync((FirstRunSetupStep)int.MaxValue, CancellationToken.None));
        Assert.False(await new OfflineLegalAccessFirstRunSetupProbe(
            new StubReader(
                new LegalCatalogReadResult.Rejected(
                    [new LegalCatalogIssue("rejected", "catalog")]),
                validTexts)).VerifyAsync(
                FirstRunSetupStep.OfflineLegalAccess,
                CancellationToken.None));
        Assert.False(await new OfflineLegalAccessFirstRunSetupProbe(
            new StubReader(
                new LegalCatalogReadResult.Available(Catalog(
                    [Component("component", [])])),
                validTexts)).VerifyAsync(
                FirstRunSetupStep.OfflineLegalAccess,
                CancellationToken.None));

        foreach (var documentResult in new LegalTextReadResult[]
                 {
                     new LegalTextReadResult.Rejected(
                         [new LegalCatalogIssue("rejected", "document")]),
                     Available(
                         component.Id,
                         new LegalDocumentReference(
                             LegalDocumentKind.Notice,
                             "NOTICES/other.txt"),
                         "text"),
                     Available(component.Id, reference, " "),
                 })
        {
            var reader = new StubReader(
                new LegalCatalogReadResult.Available(Catalog([component])),
                new Dictionary<LegalDocumentReference, LegalTextReadResult>
                {
                    [reference] = documentResult,
                });
            Assert.False(await new OfflineLegalAccessFirstRunSetupProbe(reader)
                .VerifyAsync(
                    FirstRunSetupStep.OfflineLegalAccess,
                    CancellationToken.None));
        }
    }

    private static LegalTextReadResult.Available Available(
        string componentId,
        LegalDocumentReference reference,
        string text) => new LegalTextReadResult.Available(
            new LegalTextDocument(componentId, reference, text));

    private static LegalCatalogSnapshot Catalog(
        IReadOnlyList<LegalCatalogComponent> components) => new(
            "bundle-id",
            "1.0.0",
            "sha256",
            components);

    private static LegalCatalogComponent Component(
        string id,
        IReadOnlyList<LegalDocumentReference> documents) => new(
            id,
            "Component",
            "1.0.0",
            "MIT",
            "runtime",
            "dynamic",
            Modified: false,
            "source",
            "copyright",
            documents);

    private sealed class StubReader(
        LegalCatalogReadResult catalog,
        IReadOnlyDictionary<LegalDocumentReference, LegalTextReadResult> texts)
        : ILegalCatalogReader
    {
        public List<LegalDocumentReference> ReadReferences { get; } = [];

        public Task<LegalCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken) => Task.FromResult(catalog);

        public Task<LegalTextReadResult> ReadDocumentAsync(
            string componentId,
            LegalDocumentReference reference,
            CancellationToken cancellationToken)
        {
            ReadReferences.Add(reference);
            return Task.FromResult(texts[reference]);
        }

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
