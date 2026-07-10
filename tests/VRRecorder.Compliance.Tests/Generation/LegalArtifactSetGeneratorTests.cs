using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class LegalArtifactSetGeneratorTests
{
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
            LegalFiles:
            [
                new VerifiedLegalFile(
                    LegalFileKind.License,
                    $"LICENSES/{id}/LICENSE.txt",
                    Hash(Encoding.UTF8.GetBytes(legalText)),
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
