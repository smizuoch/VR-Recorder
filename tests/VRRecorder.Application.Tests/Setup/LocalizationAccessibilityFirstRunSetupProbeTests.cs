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
        public Task<LegalCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<LegalCatalogReadResult>(
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
                        [localization, m3])])));

        public Task<LegalTextReadResult> ReadDocumentAsync(
            string componentId,
            LegalDocumentReference reference,
            CancellationToken cancellationToken) =>
            Task.FromResult<LegalTextReadResult>(
                new LegalTextReadResult.Available(new LegalTextDocument(
                    componentId,
                    reference,
                    reference == localization ? localizationText : m3Text)));

        public Task<LegalTextReadResult> ReadLicenseTextAsync(
            string componentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
