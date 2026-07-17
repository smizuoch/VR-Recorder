using System.Security.Cryptography;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class RepositoryApprovedReleaseGraphBuilderTests
{
    [Fact]
    public void ApprovedCanonicalRegistryProducesTheOnlyReleaseGraph()
    {
        using var fixture = Fixture.Create(approved: true);

        var result = Build(fixture.Root);

        Assert.True(result.IsApproved);
        Assert.Empty(result.Issues);
        var approved = Assert.IsType<ApprovedReleaseGraph>(
            result.ApprovedGraph);
        var component = Assert.Single(approved.Graph.Components);
        Assert.Equal("runtime", component.Id);
        Assert.Equal("1.2.3", component.Version);
        Assert.Equal(NoticeScope.RuntimeBundled, component.Scope);
        Assert.Equal("LEGAL-123", component.Approval.TicketId);
        Assert.Equal("requester", component.Approval.RequestedBy);
        Assert.Equal("reviewer", component.Approval.Reviewer);
        var license = Assert.Single(component.LegalFiles);
        Assert.Equal(LegalFileKind.License, license.Kind);
        Assert.Equal("LICENSES/runtime/LICENSE.txt", license.RelativePath);
    }

    [Fact]
    public void PendingReviewNeverProducesAnApprovedGraph()
    {
        using var fixture = Fixture.Create(approved: false);

        var result = Build(fixture.Root);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "pending-independent-review" &&
            issue.Subject == "runtime");
    }

    [Fact]
    public void ChangedLegalBytesInvalidateCanonicalEvidence()
    {
        using var fixture = Fixture.Create(approved: true);
        File.AppendAllText(fixture.LicensePath, "tampered");

        var result = Build(fixture.Root);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "legal-file-hash-mismatch");
    }

    [Fact]
    public void CandidateEvidenceReadFailureIsFailClosed()
    {
        using var fixture = Fixture.Create(approved: true);

        var result = RepositoryApprovedReleaseGraphBuilder.Build(
            fixture.Root,
            _ => throw new IOException("synthetic read failure"));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "repository-candidate-evidence-read-failed");
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, bool approved)
        {
            Root = root;
            var thirdParty = Path.Combine(root, "third-party");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            LicensePath = Path.Combine(thirdParty, "licenses", "runtime.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            File.WriteAllText(LicensePath, "MIT license\n");
            var licenseSha = Sha256(File.ReadAllBytes(LicensePath));
            File.WriteAllText(
                Path.Combine(thirdParty, "runtime-load-manifest.yml"),
                "{\"schemaVersion\":1,\"entries\":[]}");
            File.WriteAllText(
                Path.Combine(thirdParty, "native-link-manifest.yml"),
                "{\"schemaVersion\":1,\"entries\":[]}");
            var approval = approved
                ? "{\"status\":\"approved\",\"id\":\"LEGAL-123\",\"requestedBy\":\"requester\",\"reviewer\":\"reviewer\"}"
                : "{\"status\":\"pending-independent-review\",\"id\":null,\"requestedBy\":\"requester\",\"reviewer\":null}";
            File.WriteAllText(
                Path.Combine(thirdParty, "registry.yml"),
                $$"""
                {"schemaVersion":1,"registryVersion":1,"components":[{"id":"runtime","displayName":"Runtime","version":"1.2.3","purl":"pkg:generic/runtime@1.2.3","scope":"runtime-bundled","licenseDeclared":"MIT","licenseConcluded":"MIT","copyrightNotice":"Copyright Example","licenseFilePath":"third-party/licenses/runtime.txt","licenseFileSha256":"{{licenseSha}}","repository":{"url":"https://example.invalid/runtime","commit":"commit"},"modified":false,"approval":{{approval}},"packages":[]}]}
                """);
        }

        public string Root { get; }

        public string LicensePath { get; }

        public static Fixture Create(bool approved)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "vrrecorder-approved-graph-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Fixture(root, approved);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string Sha256(byte[] bytes) => Convert
            .ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
    }

    private static ReleaseEligibilityResult Build(string root) =>
        RepositoryApprovedReleaseGraphBuilder.Build(root, _ => []);
}
