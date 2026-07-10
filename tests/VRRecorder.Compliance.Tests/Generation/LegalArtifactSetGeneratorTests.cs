using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class LegalArtifactSetGeneratorTests
{
    [Fact]
    public void ReorderedGraphProducesByteIdenticalArtifactSet()
    {
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace: "https://example.invalid/spdx/artifact-set",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");
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
}
