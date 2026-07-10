using VRRecorder.Application.Compliance;

namespace VRRecorder.Application.Tests.Compliance;

public sealed class LegalCatalogContractsTests
{
    [Fact]
    public void SnapshotCarriesAuthenticatedManifestIdentitySeparately()
    {
        var component = Component();
        var manifestSha256 = new string('a', 64);

        var snapshot = new LegalCatalogSnapshot(
            "https://example.invalid/spdx/legal-contract",
            "0.1.0",
            manifestSha256,
            [component]);

        Assert.Equal(manifestSha256, snapshot.ManifestSha256);
        Assert.Same(component, Assert.Single(snapshot.Components));
    }

    [Fact]
    public void LicensePathIsDerivedFromTypedDocumentReference()
    {
        var component = Component();

        Assert.Equal(
            "LICENSES/example/LICENSE.txt",
            component.LicenseTextPath);
        Assert.Contains(component.LegalDocuments, reference =>
            reference.Kind == LegalDocumentKind.Notice &&
            reference.RelativePath == "NOTICES/example/NOTICE.txt");
    }

    private static LegalCatalogComponent Component() =>
        new(
            "example",
            "Example",
            "1.0.0",
            "MIT",
            "runtime",
            "managed-library",
            false,
            "offline source@example",
            "Copyright Example",
            [
                new LegalDocumentReference(
                    LegalDocumentKind.License,
                    "LICENSES/example/LICENSE.txt"),
                new LegalDocumentReference(
                    LegalDocumentKind.Notice,
                    "NOTICES/example/NOTICE.txt"),
            ]);
}
