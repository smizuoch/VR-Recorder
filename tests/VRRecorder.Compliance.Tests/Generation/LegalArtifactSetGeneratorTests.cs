using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Runtime;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class LegalArtifactSetGeneratorTests
{
    private static readonly string[] ExpectedComponentIds = ["a", "b"];

    [Fact]
    public async Task RewritingBundleRemovesArtifactsNoLongerGenerated()
    {
        using var directory = TemporaryDirectory.Create();
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var first = LegalArtifactSetGenerator.Generate(
            Context("stale-removal"),
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            first,
            CancellationToken.None);
        var removed = first.Artifacts.Single(
            artifact => artifact.RelativePath == "LICENSES/b/LICENSE.txt");
        var replacement = new GeneratedLegalArtifactSet(first.Artifacts
            .Where(artifact => artifact != removed)
            .ToArray());

        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            replacement,
            CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(
            directory.Path,
            "LICENSES",
            "b",
            "LICENSE.txt")));
        Assert.Empty(await LegalArtifactDirectoryVerifier.VerifyAsync(
            directory.Path,
            replacement,
            CancellationToken.None));
    }

    [Fact]
    public async Task FailedRewriteLeavesPreviouslyVerifiedBundleUntouched()
    {
        using var directory = TemporaryDirectory.Create();
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var baseline = LegalArtifactSetGenerator.Generate(
            Context("rewrite-rollback"),
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            baseline,
            CancellationToken.None);
        var changedContent = Encoding.UTF8.GetBytes("changed before failure");
        var invalidReplacement = new GeneratedLegalArtifactSet(
        [
            new GeneratedLegalArtifact(
                "THIRD-PARTY-NOTICES.txt",
                changedContent,
                Hash(changedContent)),
            new GeneratedLegalArtifact(
                "../escape.txt",
                Encoding.UTF8.GetBytes("invalid"),
                Hash(Encoding.UTF8.GetBytes("invalid"))),
        ]);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            LegalArtifactDirectoryWriter.WriteAsync(
                directory.Path,
                invalidReplacement,
                CancellationToken.None));

        Assert.Empty(await LegalArtifactDirectoryVerifier.VerifyAsync(
            directory.Path,
            baseline,
            CancellationToken.None));
    }

    [Fact]
    public async Task UnexpectedFileIsRejectedByDirectoryVerification()
    {
        using var directory = TemporaryDirectory.Create();
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var expected = LegalArtifactSetGenerator.Generate(
            Context("unexpected-file"),
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            expected,
            CancellationToken.None);
        var unexpectedPath = Path.Combine(
            directory.Path,
            "LICENSES",
            "stale-component",
            "LICENSE.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(unexpectedPath)!);
        await File.WriteAllTextAsync(unexpectedPath, "stale license");

        var issues = await LegalArtifactDirectoryVerifier.VerifyAsync(
            directory.Path,
            expected,
            CancellationToken.None);

        var issue = Assert.Single(issues);
        Assert.Equal("generated-artifact-unexpected", issue.Code);
        Assert.Equal("LICENSES/stale-component/LICENSE.txt", issue.Subject);
    }

    [Fact]
    public async Task GeneratedBundlePassesAuthenticatedRuntimeVerification()
    {
        using var directory = TemporaryDirectory.Create();
        var context = Context("runtime-verification");
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var generated = LegalArtifactSetGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            generated,
            CancellationToken.None);
        var manifest = Assert.Single(generated.Artifacts, artifact =>
            artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        var verifier = new AuthenticatedLegalBundleVerifier(
            new FixedAuthenticatedAnchorSource(
                new AuthenticatedLegalBundleAnchor(
                    context.DocumentNamespace,
                    manifest.Sha256)));

        var result = await verifier.VerifyAsync(
            directory.Path,
            CancellationToken.None);

        var verified = Assert.IsType<LegalBundleVerification.Verified>(result);
        Assert.Equal(context.DocumentNamespace, verified.Identity.BundleId);
        Assert.Equal(manifest.Sha256, verified.Identity.ManifestSha256);
    }

    [Fact]
    public void ArtifactSetIncludesOfflineHtmlNoticesWithContentsAndFullText()
    {
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var generated = LegalArtifactSetGenerator.Generate(
            Context("html-notices"),
            eligibility.ApprovedGraph!);

        var artifact = Assert.Single(
            generated.Artifacts,
            item => item.RelativePath == "THIRD-PARTY-NOTICES.html");
        var html = Encoding.UTF8.GetString(artifact.Content.Span);

        Assert.StartsWith("<!doctype html>\n", html, StringComparison.Ordinal);
        Assert.Contains("<nav aria-label=\"Contents\">", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#component-a\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#component-b\"", html, StringComparison.Ordinal);
        Assert.Contains("<section id=\"component-a\">", html, StringComparison.Ordinal);
        Assert.Contains("<pre>a LICENSE\n</pre>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" src=", html, StringComparison.OrdinalIgnoreCase);

        var manifest = generated.Artifacts.Single(
            item => item.RelativePath == "LEGAL-MANIFEST.sha256");
        Assert.Contains(
            $"{artifact.Sha256}  THIRD-PARTY-NOTICES.html\n",
            Encoding.UTF8.GetString(manifest.Content.Span),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactSetIncludesDeterministicManifestForEveryPayloadFile()
    {
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var generated = LegalArtifactSetGenerator.Generate(
            Context("manifest"),
            eligibility.ApprovedGraph!);

        var manifest = Assert.Single(
            generated.Artifacts,
            artifact => artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        var payloads = generated.Artifacts
            .Where(artifact => artifact.RelativePath != manifest.RelativePath)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var expected = string.Concat(payloads.Select(artifact =>
            $"{Hash(artifact.Content.Span)}  {artifact.RelativePath}\n"));
        var actual = Encoding.UTF8.GetString(manifest.Content.Span);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain(manifest.RelativePath, actual, StringComparison.Ordinal);
        var lines = actual.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(payloads.Length, lines.Length);
        Assert.All(lines, line =>
        {
            Assert.Equal(' ', line[64]);
            Assert.Equal(' ', line[65]);
            Assert.All(line[..64], character => Assert.True(
                character is >= '0' and <= '9' or >= 'a' and <= 'f'));
        });
    }

    [Fact]
    public void ArtifactSetIncludesV3ComponentCatalogCoveredByManifest()
    {
        var context = Context("component-catalog-v3");
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: true));

        var generated = LegalArtifactSetGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);

        var catalog = Assert.Single(
            generated.Artifacts,
            artifact => artifact.RelativePath ==
                        "THIRD-PARTY-COMPONENTS.json");
        using var catalogDocument = JsonDocument.Parse(catalog.Content);
        var root = catalogDocument.RootElement;
        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            context.DocumentNamespace,
            root.GetProperty("bundleId").GetString());
        Assert.Equal(
            context.ProductVersion,
            root.GetProperty("productVersion").GetString());
        Assert.Equal(
            "2026-07-10T00:00:00Z",
            root.GetProperty("generatedAtUtc").GetString());
        Assert.False(root.TryGetProperty("manifestSha256", out _));
        var integrityManifest = root.GetProperty("integrityManifest");
        Assert.Equal(
            "LEGAL-MANIFEST.sha256",
            integrityManifest.GetProperty("path").GetString());
        Assert.Equal(
            "SHA-256",
            integrityManifest.GetProperty("algorithm").GetString());
        var components = root.GetProperty("components")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(ExpectedComponentIds, components.Select(component =>
            component.GetProperty("id").GetString()));
        Assert.Collection(
            components,
            component => AssertCatalogComponent(
                component,
                "a",
                "LICENSES/a/LICENSE.txt",
                expectedDocumentCount: 4),
            component => AssertCatalogComponent(
                component,
                "b",
                "LICENSES/b/LICENSE.txt",
                expectedDocumentCount: 1));

        var firstDocuments = components[0]
            .GetProperty("legalDocuments")
            .EnumerateArray()
            .Select(document => (
                Kind: document.GetProperty("kind").GetString(),
                Path: document.GetProperty("path").GetString()))
            .ToArray();
        Assert.Equal(
        [
            ("license", "LICENSES/a/LICENSE.txt"),
            ("notice", "NOTICES/a/NOTICE.txt"),
            ("copyright", "COPYRIGHTS/a.txt"),
            ("attribution", "RIGHTS/a-attribution.txt"),
        ],
            firstDocuments);

        var sbom = generated.Artifacts.Single(artifact =>
            artifact.RelativePath == "SBOM/manifest.spdx.json");
        using var sbomDocument = JsonDocument.Parse(sbom.Content);
        Assert.Equal(
            root.GetProperty("bundleId").GetString(),
            sbomDocument.RootElement
                .GetProperty("documentNamespace")
                .GetString());
        var manifest = generated.Artifacts.Single(artifact =>
            artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        Assert.Contains(
            $"{catalog.Sha256}  THIRD-PARTY-COMPONENTS.json\n",
            Encoding.UTF8.GetString(manifest.Content.Span),
            StringComparison.Ordinal);
        var manifestText = Encoding.UTF8.GetString(manifest.Content.Span);
        Assert.All(firstDocuments, document => Assert.Contains(
            $"  {document.Path}\n",
            manifestText,
            StringComparison.Ordinal));
    }

    [Fact]
    public void EligibilityRejectsMissingCopyrightNoticeBeforeGeneration()
    {
        var graph = Graph(reverse: false);
        graph = graph with
        {
            Components =
            [
                graph.Components[0] with { CopyrightNotice = " " },
                .. graph.Components.Skip(1),
            ],
        };
        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.False(eligibility.IsApproved);
        Assert.Null(eligibility.ApprovedGraph);
        var issue = Assert.Single(eligibility.Issues, item =>
            item.Code == "missing-copyright-notice");
        Assert.Equal("a", issue.Subject);
    }

    [Fact]
    public void ComponentCatalogAllowsSameKindAtDifferentPathsDeterministically()
    {
        var graph = Graph(reverse: false);
        var first = graph.Components[0];
        var additionalNotice = LegalFile(
            LegalFileKind.Notice,
            "NOTICES/a/ADDITIONAL-NOTICE.txt",
            "additional notice\n");
        graph = graph with
        {
            Components =
            [
                first with
                {
                    LegalFiles = [.. first.LegalFiles, additionalNotice],
                },
                .. graph.Components.Skip(1),
            ],
        };
        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.True(eligibility.IsApproved);

        var generated = LegalArtifactSetGenerator.Generate(
            Context("same-document-kind"),
            eligibility.ApprovedGraph!);
        var catalog = generated.Artifacts.Single(artifact =>
            artifact.RelativePath == "THIRD-PARTY-COMPONENTS.json");
        using var document = JsonDocument.Parse(catalog.Content);
        var notices = document.RootElement.GetProperty("components")[0]
            .GetProperty("legalDocuments")
            .EnumerateArray()
            .Where(item => item.GetProperty("kind").GetString() == "notice")
            .Select(item => item.GetProperty("path").GetString()!)
            .ToArray();

        Assert.Equal(
        [
            "NOTICES/a/ADDITIONAL-NOTICE.txt",
            "NOTICES/a/NOTICE.txt",
        ],
            notices);
    }

    [Fact]
    public void EligibilityRejectsLegalDocumentPathCollision()
    {
        var graph = Graph(reverse: false);
        var first = graph.Components[0];
        var collision = LegalFile(
            LegalFileKind.Attribution,
            "NOTICES/a/NOTICE.txt",
            "colliding path\n");
        graph = graph with
        {
            Components =
            [
                first with
                {
                    LegalFiles = [.. first.LegalFiles, collision],
                },
                .. graph.Components.Skip(1),
            ],
        };

        var eligibility = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(eligibility.IsApproved);
        Assert.Null(eligibility.ApprovedGraph);
        var issue = Assert.Single(eligibility.Issues, item =>
            item.Code == "duplicate-legal-document-path");
        Assert.Equal("a:NOTICES/a/NOTICE.txt", issue.Subject);
    }

    [Fact]
    public void EligibilityRejectsCrossComponentDocumentPathCollision()
    {
        var graph = Graph(reverse: false);
        var second = graph.Components[1];
        var collidingLicense = LegalFile(
            LegalFileKind.License,
            "LICENSES/a/LICENSE.txt",
            "replacement license\n");
        graph = graph with
        {
            Components =
            [
                graph.Components[0],
                second with { LegalFiles = [collidingLicense] },
            ],
        };

        var eligibility = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(eligibility.IsApproved);
        Assert.Null(eligibility.ApprovedGraph);
        var issue = Assert.Single(eligibility.Issues, item =>
            item.Code == "conflicting-legal-document-path");
        Assert.Equal("a,b:LICENSES/a/LICENSE.txt", issue.Subject);
    }

    [Fact]
    public void IdenticalDocumentCanBeSharedAcrossComponents()
    {
        var graph = Graph(reverse: false);
        var firstLicense = graph.Components[0].LegalFiles.Single(file =>
            file.Kind == LegalFileKind.License);
        graph = graph with
        {
            Components =
            [
                graph.Components[0],
                graph.Components[1] with { LegalFiles = [firstLicense] },
            ],
        };

        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.True(eligibility.IsApproved);
        Assert.NotNull(eligibility.ApprovedGraph);
        var generated = LegalArtifactSetGenerator.Generate(
            Context("shared-license"),
            eligibility.ApprovedGraph);
        Assert.Single(generated.Artifacts, artifact =>
            artifact.RelativePath == "LICENSES/a/LICENSE.txt");
        var manifest = generated.Artifacts.Single(artifact =>
            artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        Assert.Single(Encoding.UTF8.GetString(manifest.Content.Span)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries), line =>
                line.EndsWith(
                    "  LICENSES/a/LICENSE.txt",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void LegalFileKindDefinesAssetManifest()
    {
        Assert.True(Enum.IsDefined(
            typeof(LegalFileKind),
            "AssetManifest"));
    }

    [Fact]
    public async Task ManualNoticeEditIsDetectedByRegenerationDiff()
    {
        using var directory = TemporaryDirectory.Create();
        var context = Context("manual-edit");
        var eligibility = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var expected = LegalArtifactSetGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            expected,
            CancellationToken.None);
        var noticePath = Path.Combine(
            directory.Path,
            "THIRD-PARTY-NOTICES.txt");
        await File.AppendAllTextAsync(noticePath, "MANUAL EDIT");

        var issues = await LegalArtifactDirectoryVerifier.VerifyAsync(
            directory.Path,
            expected,
            CancellationToken.None);

        var issue = Assert.Single(issues);
        Assert.Equal("generated-artifact-diff", issue.Code);
        Assert.Equal("THIRD-PARTY-NOTICES.txt", issue.Subject);
    }

    [Fact]
    public void ReorderedGraphProducesByteIdenticalArtifactSet()
    {
        var context = Context("artifact-set");
        var forward = ReleaseEligibilityGate.Evaluate(Graph(reverse: false));
        var reverse = ReleaseEligibilityGate.Evaluate(Graph(reverse: true));
        Assert.True(forward.IsApproved);
        Assert.True(reverse.IsApproved);

        var first = LegalArtifactSetGenerator.Generate(
            context,
            forward.ApprovedGraph!);
        var second = LegalArtifactSetGenerator.Generate(
            context,
            reverse.ApprovedGraph!);

        Assert.Equal(
            first.Artifacts.Select(artifact => artifact.RelativePath),
            second.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.Contains(
            first.Artifacts,
            artifact => artifact.RelativePath == "THIRD-PARTY-NOTICES.txt");
        Assert.Contains(
            first.Artifacts,
            artifact => artifact.RelativePath == "SBOM/manifest.spdx.json");
        Assert.Contains(
            first.Artifacts,
            artifact => artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        Assert.All(first.Artifacts.Zip(second.Artifacts), pair =>
        {
            Assert.Equal(pair.First.Sha256, pair.Second.Sha256);
            Assert.Equal(
                pair.First.Content.ToArray(),
                pair.Second.Content.ToArray());
            Assert.Equal(
                Hash(pair.First.Content.Span),
                pair.First.Sha256);
        });
    }

    private static SpdxGenerationContext Context(string suffix) =>
        new(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace: $"https://example.invalid/spdx/{suffix}",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");

    private static NormalizedComponentGraph Graph(bool reverse)
    {
        NuGetPackage[] dependencies =
        [
            new("Package.A", "1.0.0", NuGetDependencyKind.Direct),
            new("Package.B", "2.0.0", NuGetDependencyKind.Transitive),
        ];
        NormalizedComponent[] components =
        [
            Component("a", "Package.A", "1.0.0"),
            Component("b", "Package.B", "2.0.0"),
        ];
        return reverse
            ? new NormalizedComponentGraph(
                dependencies.Reverse().ToArray(),
                components.Reverse().ToArray())
            : new NormalizedComponentGraph(dependencies, components);
    }

    private static NormalizedComponent Component(
        string id,
        string packageId,
        string version)
    {
        var legalText = $"{id} LICENSE\n";
        return new NormalizedComponent(
            Id: id,
            DisplayName: $"Component {id}",
            Version: version,
            License: new LicenseDecision("MIT", "MIT"),
            CopyrightNotice: $"Copyright (c) Component {id}",
            Usage: "runtime-feature",
            Linkage: "managed-library",
            Modified: false,
            SourceInformation: $"https://example.invalid/{id}@commit",
            LicenseText: legalText,
            LegalFiles: id == "a"
                ?
                [
                    LegalFile(
                        LegalFileKind.License,
                        $"LICENSES/{id}/LICENSE.txt",
                        legalText),
                    LegalFile(
                        LegalFileKind.Notice,
                        $"NOTICES/{id}/NOTICE.txt",
                        $"{id} notice\n"),
                    LegalFile(
                        LegalFileKind.Copyright,
                        $"COPYRIGHTS/{id}.txt",
                        $"{id} copyright\n"),
                    LegalFile(
                        LegalFileKind.Attribution,
                        $"RIGHTS/{id}-attribution.txt",
                        $"{id} attribution\n"),
                ]
                :
                [
                    LegalFile(
                        LegalFileKind.License,
                        $"LICENSES/{id}/LICENSE.txt",
                        legalText),
                ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: $"LEGAL-{id}",
                RequestedBy: "developer",
                Reviewer: "license-reviewer"),
            Packages: [new NoticePackage(packageId, version)]);
    }

    private static void AssertCatalogComponent(
        JsonElement component,
        string id,
        string licensePath,
        int expectedDocumentCount)
    {
        Assert.Equal(id, component.GetProperty("id").GetString());
        Assert.Equal(
            $"Component {id}",
            component.GetProperty("displayName").GetString());
        Assert.Equal("MIT", component.GetProperty("licenseExpression").GetString());
        Assert.Equal(
            "runtime-feature",
            component.GetProperty("usage").GetString());
        Assert.Equal(
            "managed-library",
            component.GetProperty("linkage").GetString());
        Assert.False(component.GetProperty("modified").GetBoolean());
        Assert.Equal(
            $"Copyright (c) Component {id}",
            component.GetProperty("copyrightNotice").GetString());
        Assert.False(component.TryGetProperty("licenseText", out _));
        var documents = component.GetProperty("legalDocuments")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(expectedDocumentCount, documents.Length);
        var license = Assert.Single(documents, document =>
            document.GetProperty("kind").GetString() == "license");
        Assert.Equal(licensePath, license.GetProperty("path").GetString());
        Assert.Equal(
            $"https://example.invalid/{id}@commit",
            component.GetProperty("sourceInfo").GetString());
    }

    private static VerifiedLegalFile LegalFile(
        LegalFileKind kind,
        string path,
        string text) =>
        new(kind, path, Hash(Encoding.UTF8.GetBytes(text)), text);

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

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

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
                $"vr-recorder-legal-tests-{Guid.NewGuid():N}");
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
