using System.Security.Cryptography;
using System.Text;
using VRRecorder.Application.Compliance;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Runtime;

namespace VRRecorder.IntegrationTests.Compliance;

public sealed class AuthenticatedLegalCatalogV3IntegrationTests
{
    [Fact]
    public async Task GeneratedV3BundleReadsEveryTypedDocumentAndFailsClosedOnTamper()
    {
        using var directory = TemporaryDirectory.Create();
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace:
                "https://example.invalid/spdx/legal-catalog-v3-integration",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");
        var texts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LICENSES/example/LICENSE.txt"] = "license text\n",
            ["NOTICES/example/NOTICE.txt"] = "notice text\n",
            ["COPYRIGHTS/example.txt"] = "copyright document\n",
            ["RIGHTS/example-attribution.txt"] = "attribution text\n",
            ["MATERIAL-SYMBOLS-MANIFEST.json"] =
                "{\"schemaVersion\":2,\"icons\":[]}\n",
        };
        var graph = new NormalizedComponentGraph(
            [new NuGetPackage(
                "Package.Example",
                "1.0.0",
                NuGetDependencyKind.Direct)],
            [Component(texts)]);
        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.True(eligibility.IsApproved);
        var artifacts = LegalArtifactSetGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            artifacts,
            CancellationToken.None);
        var manifest = artifacts.Artifacts.Single(artifact =>
            artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        var reader = new AuthenticatedLegalCatalogReader(
            directory.Path,
            new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(
                    new AuthenticatedLegalBundleAnchor(
                        context.DocumentNamespace,
                        manifest.Sha256))));

        var catalog = Assert.IsType<LegalCatalogReadResult.Available>(
            await reader.ReadAsync(CancellationToken.None)).Catalog;
        Assert.Equal(manifest.Sha256, catalog.ManifestSha256);
        var component = Assert.Single(catalog.Components);
        Assert.Equal("Copyright Example", component.CopyrightNotice);
        Assert.Equal(
        [
            LegalDocumentKind.License,
            LegalDocumentKind.Notice,
            LegalDocumentKind.Copyright,
            LegalDocumentKind.Attribution,
            LegalDocumentKind.AssetManifest,
        ],
            component.LegalDocuments.Select(reference => reference.Kind));

        foreach (var reference in component.LegalDocuments)
        {
            var result = await reader.ReadDocumentAsync(
                component.Id,
                reference,
                CancellationToken.None);
            var document = Assert.IsType<LegalTextReadResult.Available>(
                result).Document;
            Assert.Equal(reference, document.Reference);
            Assert.Equal(texts[reference.RelativePath], document.Text);
        }

        await File.AppendAllTextAsync(
            Path.Combine(
                directory.Path,
                "NOTICES",
                "example",
                "NOTICE.txt"),
            "tamper");
        var tampered = await reader.ReadDocumentAsync(
            component.Id,
            component.LegalDocuments.Single(reference =>
                reference.Kind == LegalDocumentKind.Notice),
            CancellationToken.None);

        var rejected = Assert.IsType<LegalTextReadResult.Rejected>(tampered);
        Assert.Contains(rejected.Issues, issue =>
            issue.Code == "legal-bundle-payload-hash-mismatch" &&
            issue.Subject == "NOTICES/example/NOTICE.txt");
    }

    private static NormalizedComponent Component(
        Dictionary<string, string> texts) =>
        new(
            Id: "example",
            DisplayName: "Example",
            Version: "1.0.0",
            License: new LicenseDecision("MIT", "MIT"),
            CopyrightNotice: "Copyright Example",
            Usage: "runtime",
            Linkage: "managed-library",
            Modified: false,
            SourceInformation: "offline source@example",
            LicenseText: texts["LICENSES/example/LICENSE.txt"],
            LegalFiles:
            [
                LegalFile(
                    LegalFileKind.License,
                    "LICENSES/example/LICENSE.txt",
                    texts),
                LegalFile(
                    LegalFileKind.Notice,
                    "NOTICES/example/NOTICE.txt",
                    texts),
                LegalFile(
                    LegalFileKind.Copyright,
                    "COPYRIGHTS/example.txt",
                    texts),
                LegalFile(
                    LegalFileKind.Attribution,
                    "RIGHTS/example-attribution.txt",
                    texts),
                LegalFile(
                    LegalFileKind.AssetManifest,
                    "MATERIAL-SYMBOLS-MANIFEST.json",
                    texts),
            ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: "LEGAL-EXAMPLE",
                RequestedBy: "developer",
                Reviewer: "independent-legal-reviewer"),
            Packages: [new NoticePackage("Package.Example", "1.0.0")]);

    private static VerifiedLegalFile LegalFile(
        LegalFileKind kind,
        string path,
        Dictionary<string, string> texts)
    {
        var text = texts[path];
        return new VerifiedLegalFile(kind, path, Hash(text), text);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    private sealed class FixedAuthenticatedAnchorSource(
        AuthenticatedLegalBundleAnchor anchor)
        : IAuthenticatedLegalBundleAnchorSource
    {
        public ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(anchor);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-legal-v3-integration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
