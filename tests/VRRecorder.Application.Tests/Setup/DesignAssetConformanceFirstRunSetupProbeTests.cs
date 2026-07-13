using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class DesignAssetConformanceFirstRunSetupProbeTests
{
    [Fact]
    public async Task AuthenticatedReleaseManifestsVerifyDesignAssets()
    {
        var material = Reference("MATERIAL-SYMBOLS-MANIFEST.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        var reader = new StubReader(
            Catalog([material, m3]),
            new Dictionary<LegalDocumentReference, string>
            {
                [material] =
                    """
                    {"schemaVersion":2,"documentStatus":"APPROVED RELEASE MANIFEST","componentId":"material-symbols"}
                    """,
                [m3] =
                    """
                    {"schemaVersion":2,"evaluated":true,"releaseEligible":true,"summary":{"sourceInventoryCoveragePercent":100,"unclassifiedSourceEntries":0,"deferredEntriesForShippedFeatures":0,"unresolvedDeviations":0}}
                    """,
            });
        var probe = new DesignAssetConformanceFirstRunSetupProbe(reader);

        Assert.True(await probe.VerifyAsync(
            FirstRunSetupStep.DesignAssetConformance,
            CancellationToken.None));
        Assert.Equal([material, m3], reader.ReadReferences);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UnevaluatedOrIneligibleM3ReportDoesNotVerify(
        bool evaluated,
        bool releaseEligible)
    {
        var material = Reference("MATERIAL-SYMBOLS-MANIFEST.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        var reader = new StubReader(
            Catalog([material, m3]),
            new Dictionary<LegalDocumentReference, string>
            {
                [material] =
                    """
                    {"schemaVersion":2,"documentStatus":"APPROVED RELEASE MANIFEST","componentId":"material-symbols"}
                    """,
                [m3] =
                    "{\"schemaVersion\":2,\"evaluated\":" +
                    evaluated.ToString().ToLowerInvariant() +
                    ",\"releaseEligible\":" +
                    releaseEligible.ToString().ToLowerInvariant() +
                    ",\"summary\":{\"sourceInventoryCoveragePercent\":100," +
                    "\"unclassifiedSourceEntries\":0," +
                    "\"deferredEntriesForShippedFeatures\":0," +
                    "\"unresolvedDeviations\":0}}",
            });
        var probe = new DesignAssetConformanceFirstRunSetupProbe(reader);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.DesignAssetConformance,
            CancellationToken.None));
    }

    [Fact]
    public async Task DesignTimeExampleManifestDoesNotVerifyReleaseAsset()
    {
        var material = Reference("MATERIAL-SYMBOLS-MANIFEST.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        var reader = new StubReader(
            Catalog([material, m3]),
            new Dictionary<LegalDocumentReference, string>
            {
                [material] =
                    """
                    {"schemaVersion":2,"documentStatus":"DESIGN-TIME EXAMPLE — NOT A RELEASE MANIFEST","componentId":"material-symbols"}
                    """,
                [m3] =
                    """
                    {"schemaVersion":2,"evaluated":true,"releaseEligible":true,"summary":{"sourceInventoryCoveragePercent":100,"unclassifiedSourceEntries":0,"deferredEntriesForShippedFeatures":0,"unresolvedDeviations":0}}
                    """,
            });
        var probe = new DesignAssetConformanceFirstRunSetupProbe(reader);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.DesignAssetConformance,
            CancellationToken.None));
    }

    private static LegalDocumentReference Reference(string path) => new(
        LegalDocumentKind.AssetManifest,
        path);

    private static LegalCatalogReadResult.Available Catalog(
        IReadOnlyList<LegalDocumentReference> references) =>
        new LegalCatalogReadResult.Available(new LegalCatalogSnapshot(
            "bundle",
            "1.0.0",
            "sha256",
            [new LegalCatalogComponent(
                "design-assets",
                "Design assets",
                "1.0.0",
                "Apache-2.0",
                "runtime",
                "asset",
                Modified: false,
                "source",
                "copyright",
                references)]));

    private sealed class StubReader(
        LegalCatalogReadResult catalog,
        IReadOnlyDictionary<LegalDocumentReference, string> texts)
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
            return Task.FromResult<LegalTextReadResult>(
                new LegalTextReadResult.Available(
                    new LegalTextDocument(componentId, reference, texts[reference])));
        }

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
