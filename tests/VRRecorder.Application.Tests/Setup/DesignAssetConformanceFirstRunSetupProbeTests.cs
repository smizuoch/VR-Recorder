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

    [Fact]
    public async Task MaterialAndM3DocumentsRequireEveryReleaseField()
    {
        const string material =
            "{\"schemaVersion\":2,\"documentStatus\":\"APPROVED RELEASE MANIFEST\",\"componentId\":\"material-symbols\"}";
        const string m3 =
            "{\"schemaVersion\":2,\"evaluated\":true,\"releaseEligible\":true,\"summary\":{\"sourceInventoryCoveragePercent\":100,\"unclassifiedSourceEntries\":0,\"deferredEntriesForShippedFeatures\":0,\"unresolvedDeviations\":0}}";
        var materialMutations = new (string Required, string Invalid)[]
        {
            ("\"schemaVersion\":2", "\"schemaVersion\":1"),
            ("\"documentStatus\":\"APPROVED RELEASE MANIFEST\"", "\"documentStatus\":\"DRAFT\""),
            ("\"componentId\":\"material-symbols\"", "\"componentId\":\"other\""),
        };
        foreach (var (required, invalid) in materialMutations)
        {
            Assert.False(await VerifyAsync(
                material.Replace(required, invalid, StringComparison.Ordinal),
                m3));
        }
        Assert.False(await VerifyAsync("[]", m3));
        Assert.False(await VerifyAsync("{", m3));

        var m3Mutations = new (string Required, string Invalid)[]
        {
            ("\"schemaVersion\":2", "\"schemaVersion\":1"),
            ("\"evaluated\":true", "\"evaluated\":false"),
            ("\"releaseEligible\":true", "\"releaseEligible\":false"),
            ("\"summary\":{", "\"summary\":null,\"ignored\":{"),
            ("\"sourceInventoryCoveragePercent\":100", "\"sourceInventoryCoveragePercent\":99"),
            ("\"unclassifiedSourceEntries\":0", "\"unclassifiedSourceEntries\":1"),
            ("\"deferredEntriesForShippedFeatures\":0", "\"deferredEntriesForShippedFeatures\":1"),
            ("\"unresolvedDeviations\":0", "\"unresolvedDeviations\":1"),
        };
        foreach (var (required, invalid) in m3Mutations)
        {
            Assert.False(await VerifyAsync(
                material,
                m3.Replace(required, invalid, StringComparison.Ordinal)));
        }
        Assert.False(await VerifyAsync(material, "[]"));
        Assert.False(await VerifyAsync(material, "{"));
    }

    [Fact]
    public async Task CatalogAndDocumentAuthenticationFailuresDoNotVerify()
    {
        var material = Reference("MATERIAL-SYMBOLS-MANIFEST.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        var texts = new Dictionary<LegalDocumentReference, string>
        {
            [material] = ValidMaterial(),
            [m3] = ValidM3(),
        };

        Assert.False(await new DesignAssetConformanceFirstRunSetupProbe(
            new StubReader(Catalog([material, m3]), texts)).VerifyAsync(
                (FirstRunSetupStep)int.MaxValue,
                CancellationToken.None));
        Assert.False(await new DesignAssetConformanceFirstRunSetupProbe(
            new StubReader(
                new LegalCatalogReadResult.Rejected(
                    [new LegalCatalogIssue("rejected", "catalog")]),
                texts)).VerifyAsync(
                FirstRunSetupStep.DesignAssetConformance,
                CancellationToken.None));
        Assert.False(await VerifyReaderAsync(new StubReader(
            Catalog([m3]),
            texts)));
        Assert.False(await VerifyReaderAsync(new StubReader(
            Catalog([material, material, m3]),
            texts)));

        var rejectedDocument = new StubReader(
            Catalog([material, m3]),
            texts)
        {
            DocumentResultFactory = (_, _) =>
                new LegalTextReadResult.Rejected(
                    [new LegalCatalogIssue("rejected", "document")]),
        };
        Assert.False(await VerifyReaderAsync(rejectedDocument));

        var mismatchedDocument = new StubReader(
            Catalog([material, m3]),
            texts)
        {
            DocumentResultFactory = (_, reference) =>
                new LegalTextReadResult.Available(new LegalTextDocument(
                    "wrong-component",
                    reference,
                    ValidMaterial())),
        };
        Assert.False(await VerifyReaderAsync(mismatchedDocument));
    }

    private static Task<bool> VerifyAsync(
        string materialText,
        string m3Text)
    {
        var material = Reference("MATERIAL-SYMBOLS-MANIFEST.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        return VerifyReaderAsync(new StubReader(
            Catalog([material, m3]),
            new Dictionary<LegalDocumentReference, string>
            {
                [material] = materialText,
                [m3] = m3Text,
            }));
    }

    private static Task<bool> VerifyReaderAsync(ILegalCatalogReader reader) =>
        new DesignAssetConformanceFirstRunSetupProbe(reader).VerifyAsync(
            FirstRunSetupStep.DesignAssetConformance,
            CancellationToken.None);

    private static string ValidMaterial() =>
        "{\"schemaVersion\":2,\"documentStatus\":\"APPROVED RELEASE MANIFEST\",\"componentId\":\"material-symbols\"}";

    private static string ValidM3() =>
        "{\"schemaVersion\":2,\"evaluated\":true,\"releaseEligible\":true,\"summary\":{\"sourceInventoryCoveragePercent\":100,\"unclassifiedSourceEntries\":0,\"deferredEntriesForShippedFeatures\":0,\"unresolvedDeviations\":0}}";

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
        public Func<string, LegalDocumentReference, LegalTextReadResult>?
            DocumentResultFactory
        { get; init; }

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
                DocumentResultFactory?.Invoke(componentId, reference) ??
                new LegalTextReadResult.Available(
                    new LegalTextDocument(componentId, reference, texts[reference])));
        }

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
