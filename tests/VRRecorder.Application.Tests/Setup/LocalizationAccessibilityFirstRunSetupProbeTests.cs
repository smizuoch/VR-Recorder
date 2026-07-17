using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class LocalizationAccessibilityFirstRunSetupProbeTests
{
    [Fact]
    public async Task AuthenticatedLocalizationAndEligibleAccessibilityEvidenceVerify()
    {
        var localization = Reference("LOCALIZATION-CONTRACT.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        var probe = new LocalizationAccessibilityFirstRunSetupProbe(
            new StubReader(localization, m3, ValidLocalization(), ValidM3()));

        Assert.True(await probe.VerifyAsync(
            FirstRunSetupStep.LocalizationAccessibility,
            CancellationToken.None));
    }

    [Theory]
    [InlineData("qps-ploc", 100, "ltr")]
    [InlineData("qps-plocm", 200, "ltr")]
    public async Task WrongPseudoExpansionOrDirectionDoesNotVerify(
        string locale,
        int scale,
        string direction)
    {
        var localization = Reference("LOCALIZATION-CONTRACT.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        var invalid = ValidLocalization()
            .Replace(
                $"\"locale\":\"{locale}\",\"scalePercent\":200,\"direction\":\"{(locale == "qps-plocm" ? "rtl" : "ltr")}\"",
                $"\"locale\":\"{locale}\",\"scalePercent\":{scale},\"direction\":\"{direction}\"",
                StringComparison.Ordinal);
        var probe = new LocalizationAccessibilityFirstRunSetupProbe(
            new StubReader(localization, m3, invalid, ValidM3()));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.LocalizationAccessibility,
            CancellationToken.None));
    }

    [Fact]
    public async Task MissingHighContrastCheckDoesNotVerify()
    {
        var localization = Reference("LOCALIZATION-CONTRACT.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        var report = ValidM3().Replace(
            ",\"high-contrast-golden-test\"",
            string.Empty,
            StringComparison.Ordinal);
        var probe = new LocalizationAccessibilityFirstRunSetupProbe(
            new StubReader(localization, m3, ValidLocalization(), report));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.LocalizationAccessibility,
            CancellationToken.None));
    }

    [Fact]
    public async Task EveryLocalizationContractRuleIsRequired()
    {
        var mutations = new (string Required, string Invalid)[]
        {
            ("\"schemaVersion\":1", "\"schemaVersion\":2"),
            ("[\"ja-JP\",\"en-US\"]", "[\"ja-JP\"]"),
            ("\"fallbackLocale\":\"en-US\"", "\"fallbackLocale\":\"ja-JP\""),
            ("\"resourceRules\":{", "\"resourceRules\":null,"),
            ("\"hardCodedUserVisibleTextAllowed\":false", "\"hardCodedUserVisibleTextAllowed\":true"),
            ("\"missingKeyBehavior\":\"fail-build\"", "\"missingKeyBehavior\":\"show-key\""),
            ("\"exposeResourceKeyToUser\":false", "\"exposeResourceKeyToUser\":true"),
            ("\"iconLigatureAsAccessibleNameAllowed\":false", "\"iconLigatureAsAccessibleNameAllowed\":true"),
            ("\"resourceParityRules\":{", "\"resourceParityRules\":null,"),
            ("\"sameKeySetRequired\":true", "\"sameKeySetRequired\":false"),
            ("\"placeholderParityRequired\":true", "\"placeholderParityRequired\":false"),
            ("\"emptyTranslationAllowed\":false", "\"emptyTranslationAllowed\":true"),
            ("\"inputEquivalence\":{", "\"inputEquivalence\":null,"),
            ("\"dragOnlyOperationAllowed\":false", "\"dragOnlyOperationAllowed\":true"),
            ("\"keyboardControllerRayParityRequired\":true", "\"keyboardControllerRayParityRequired\":false"),
            ("\"locale\":\"ja-JP\",\"scalePercent\":100", "\"locale\":\"ja-JP\",\"scalePercent\":101"),
            ("\"locale\":\"en-US\",\"scalePercent\":100", "\"locale\":\"en-US\",\"scalePercent\":101"),
        };

        foreach (var (required, invalid) in mutations)
        {
            var changed = ValidLocalization().Replace(
                required,
                invalid,
                StringComparison.Ordinal);
            Assert.NotEqual(ValidLocalization(), changed);
            Assert.False(
                await VerifyAsync(changed, ValidM3()),
                $"Mutation did not invalidate required rule: {required}");
        }

        Assert.False(await VerifyAsync("{", ValidM3()));
        Assert.False(await VerifyAsync(
            ValidLocalization().Replace(
                "\"initialLocales\":[",
                "\"initialLocales\":{},\"ignoredLocales\":[",
                StringComparison.Ordinal),
            ValidM3()));
        Assert.False(await VerifyAsync(
            ValidLocalization().Replace(
                "\"layoutTests\":[",
                "\"layoutTests\":{},\"ignored\":[",
                StringComparison.Ordinal),
            ValidM3()));
    }

    [Fact]
    public async Task EveryAccessibilityReportRuleIsRequired()
    {
        var mutations = new (string Required, string Invalid)[]
        {
            ("\"schemaVersion\":2", "\"schemaVersion\":1"),
            ("\"evaluated\":true", "\"evaluated\":false"),
            ("\"releaseEligible\":true", "\"releaseEligible\":false"),
            ("\"summary\":{", "\"summary\":null,"),
            ("\"accessibleNameCoveragePercent\":100", "\"accessibleNameCoveragePercent\":99"),
            ("\"requiredChecks\":[", "\"requiredChecks\":{},\"ignored\":["),
            ("\"tooltip-test\",", string.Empty),
            ("\"accessible-name-test\",", string.Empty),
            ("\"japanese-english-golden-test\",", string.Empty),
            ("\"pseudo-locale-golden-test\",", string.Empty),
            ("\"rtl-golden-test\",", string.Empty),
        };

        foreach (var (required, invalid) in mutations)
        {
            var changed = ValidM3().Replace(
                required,
                invalid,
                StringComparison.Ordinal);
            Assert.NotEqual(ValidM3(), changed);
            Assert.False(
                await VerifyAsync(ValidLocalization(), changed),
                $"Mutation did not invalidate required rule: {required}");
        }

        Assert.False(await VerifyAsync(ValidLocalization(), "{"));
    }

    [Fact]
    public async Task CatalogAndDocumentAuthenticationFailuresDoNotVerify()
    {
        var localization = Reference("LOCALIZATION-CONTRACT.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");

        Assert.False(await new LocalizationAccessibilityFirstRunSetupProbe(
            new StubReader(localization, m3, ValidLocalization(), ValidM3()))
            .VerifyAsync((FirstRunSetupStep)int.MaxValue, CancellationToken.None));

        var unavailable = new StubReader(
            localization,
            m3,
            ValidLocalization(),
            ValidM3())
        {
            CatalogResult = new LegalCatalogReadResult.Rejected(
                [new LegalCatalogIssue("missing-bundle", "bundle")]),
        };
        Assert.False(await new LocalizationAccessibilityFirstRunSetupProbe(
            unavailable).VerifyAsync(
                FirstRunSetupStep.LocalizationAccessibility,
                CancellationToken.None));

        Assert.False(await VerifyWithReaderAsync(new StubReader(
            Reference("OTHER.json"),
            m3,
            ValidLocalization(),
            ValidM3())));
        Assert.False(await VerifyWithReaderAsync(new StubReader(
            localization,
            m3,
            ValidLocalization(),
            ValidM3())
        {
            AdditionalReferences = [localization],
        }));

        var unavailableDocument = new StubReader(
            localization,
            m3,
            ValidLocalization(),
            ValidM3())
        {
            DocumentResultFactory = (_, _) =>
                new LegalTextReadResult.Rejected(
                    [new LegalCatalogIssue("missing-document", "document")]),
        };
        Assert.False(await VerifyWithReaderAsync(unavailableDocument));

        var mismatchedDocument = new StubReader(
            localization,
            m3,
            ValidLocalization(),
            ValidM3())
        {
            DocumentResultFactory = (_, reference) =>
                new LegalTextReadResult.Available(new LegalTextDocument(
                    "wrong-component",
                    reference,
                    ValidLocalization())),
        };
        Assert.False(await VerifyWithReaderAsync(mismatchedDocument));
    }

    private static Task<bool> VerifyAsync(
        string localizationText,
        string m3Text)
    {
        var localization = Reference("LOCALIZATION-CONTRACT.json");
        var m3 = Reference("M3-CONFORMANCE-REPORT.json");
        return VerifyWithReaderAsync(new StubReader(
            localization,
            m3,
            localizationText,
            m3Text));
    }

    private static Task<bool> VerifyWithReaderAsync(ILegalCatalogReader reader) =>
        new LocalizationAccessibilityFirstRunSetupProbe(reader).VerifyAsync(
            FirstRunSetupStep.LocalizationAccessibility,
            CancellationToken.None);

    private static string ValidLocalization() =>
        """
        {"schemaVersion":1,"initialLocales":["ja-JP","en-US"],"fallbackLocale":"en-US","resourceRules":{"hardCodedUserVisibleTextAllowed":false,"missingKeyBehavior":"fail-build","exposeResourceKeyToUser":false,"iconLigatureAsAccessibleNameAllowed":false},"layoutTests":[{"locale":"ja-JP","scalePercent":100,"direction":"ltr"},{"locale":"en-US","scalePercent":100,"direction":"ltr"},{"locale":"qps-ploc","scalePercent":200,"direction":"ltr"},{"locale":"qps-plocm","scalePercent":200,"direction":"rtl"}],"resourceParityRules":{"sameKeySetRequired":true,"placeholderParityRequired":true,"emptyTranslationAllowed":false},"inputEquivalence":{"dragOnlyOperationAllowed":false,"keyboardControllerRayParityRequired":true}}
        """;

    private static string ValidM3() =>
        """
        {"schemaVersion":2,"evaluated":true,"releaseEligible":true,"summary":{"accessibleNameCoveragePercent":100},"requiredChecks":["tooltip-test","accessible-name-test","japanese-english-golden-test","pseudo-locale-golden-test","rtl-golden-test","high-contrast-golden-test"]}
        """;

    private static LegalDocumentReference Reference(string path) => new(
        LegalDocumentKind.AssetManifest,
        path);

    private sealed class StubReader(
        LegalDocumentReference localization,
        LegalDocumentReference m3,
        string localizationText,
        string m3Text) : ILegalCatalogReader
    {
        public LegalCatalogReadResult? CatalogResult { get; init; }

        public IReadOnlyList<LegalDocumentReference> AdditionalReferences
        {
            get;
            init;
        } = [];

        public Func<string, LegalDocumentReference, LegalTextReadResult>?
            DocumentResultFactory
        { get; init; }

        public Task<LegalCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<LegalCatalogReadResult>(
                CatalogResult ??
                new LegalCatalogReadResult.Available(new LegalCatalogSnapshot(
                    "bundle",
                    "1.0.0",
                    "sha256",
                    [new LegalCatalogComponent(
                        "ui-contracts",
                        "UI contracts",
                        "1.0.0",
                        "NOASSERTION",
                        "runtime",
                        "asset",
                        Modified: false,
                        "source",
                        "copyright",
                        [localization, m3, .. AdditionalReferences])])));

        public Task<LegalTextReadResult> ReadDocumentAsync(
            string componentId,
            LegalDocumentReference reference,
            CancellationToken cancellationToken) =>
            Task.FromResult<LegalTextReadResult>(
                DocumentResultFactory?.Invoke(componentId, reference) ??
                new LegalTextReadResult.Available(new LegalTextDocument(
                    componentId,
                    reference,
                    reference == localization ? localizationText : m3Text)));

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
