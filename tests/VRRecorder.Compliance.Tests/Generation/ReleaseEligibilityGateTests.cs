using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class ReleaseEligibilityGateTests
{
    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("NOASSERTION")]
    [InlineData("NONE")]
    public void UnresolvedLicenseDoesNotProduceApprovedReleaseGraph(
        string expression)
    {
        var graph = GraphWithApproval(
            new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: "LEGAL-001",
                RequestedBy: "developer",
                Reviewer: "license-reviewer"),
            license: new LicenseDecision(expression, expression));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("unresolved-license", issue.Code);
        Assert.Equal("pending-package", issue.Subject);
    }

    [Fact]
    public void IndependentlyReviewedComponentProducesApprovedReleaseGraph()
    {
        var graph = GraphWithApproval(new LegalApproval(
            LegalApprovalStatus.Approved,
            TicketId: "LEGAL-001",
            RequestedBy: "developer",
            Reviewer: "license-reviewer"));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.True(result.IsApproved);
        Assert.Same(graph, result.ApprovedGraph!.Graph);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void LegalFileHashMismatchDoesNotProduceApprovedReleaseGraph()
    {
        var graph = GraphWithApproval(
            new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: "LEGAL-001",
                RequestedBy: "developer",
                Reviewer: "license-reviewer"),
            legalFileSha256:
                "0000000000000000000000000000000000000000000000000000000000000000");

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("legal-file-hash-mismatch", issue.Code);
        Assert.Equal(
            "pending-package:licenses/pending-package/LICENSE.txt",
            issue.Subject);
    }

    [Fact]
    public void PendingComponentDoesNotProduceApprovedReleaseGraph()
    {
        var graph = GraphWithApproval(new LegalApproval(
            LegalApprovalStatus.Pending,
            TicketId: null,
            RequestedBy: "developer",
            Reviewer: null));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("pending-independent-review", issue.Code);
        Assert.Equal("pending-package", issue.Subject);
    }

    [Fact]
    public void ApprovedComponentWithoutTicketDoesNotProduceApprovedReleaseGraph()
    {
        var graph = GraphWithApproval(new LegalApproval(
            LegalApprovalStatus.Approved,
            TicketId: null,
            RequestedBy: "developer",
            Reviewer: "license-reviewer"));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing-approval-ticket", issue.Code);
        Assert.Equal("pending-package", issue.Subject);
    }

    [Fact]
    public void ApprovedComponentWithoutReviewerDoesNotProduceApprovedReleaseGraph()
    {
        var graph = GraphWithApproval(new LegalApproval(
            LegalApprovalStatus.Approved,
            TicketId: "LEGAL-001",
            RequestedBy: "developer",
            Reviewer: null));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing-approval-reviewer", issue.Code);
        Assert.Equal("pending-package", issue.Subject);
    }

    [Fact]
    public void ApprovedComponentWithoutRequesterDoesNotProduceApprovedReleaseGraph()
    {
        var graph = GraphWithApproval(new LegalApproval(
            LegalApprovalStatus.Approved,
            TicketId: "LEGAL-001",
            RequestedBy: string.Empty,
            Reviewer: "license-reviewer"));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("missing-approval-requester", issue.Code);
        Assert.Equal("pending-package", issue.Subject);
    }

    [Fact]
    public void SelfApprovedComponentDoesNotProduceApprovedReleaseGraph()
    {
        var graph = GraphWithApproval(new LegalApproval(
            LegalApprovalStatus.Approved,
            TicketId: "LEGAL-001",
            RequestedBy: "Developer-One",
            Reviewer: "developer-one"));

        var result = ReleaseEligibilityGate.Evaluate(graph);

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovedGraph);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("self-approval", issue.Code);
        Assert.Equal("pending-package", issue.Subject);
    }

    private static NormalizedComponentGraph GraphWithApproval(
        LegalApproval approval,
        string legalFileSha256 = ValidLegalFileSha256,
        LicenseDecision? license = null) =>
        new(
            Dependencies:
            [
                new NuGetPackage(
                    "Pending.Package",
                    "1.0.0",
                    NuGetDependencyKind.Direct),
            ],
            Components:
            [
                new NormalizedComponent(
                    Id: "pending-package",
                    DisplayName: "Pending Package",
                    Version: "1.0.0",
                    License: license ?? new LicenseDecision("MIT", "MIT"),
                    CopyrightNotice: "Copyright (c) Example",
                    Usage: "runtime-feature",
                    Linkage: "managed-library",
                    Modified: false,
                    SourceInformation: "https://example.invalid/source@commit",
                    LicenseText: "FULL MIT LICENSE TEXT",
                    LegalFiles:
                    [
                        new VerifiedLegalFile(
                            LegalFileKind.License,
                            "licenses/pending-package/LICENSE.txt",
                            legalFileSha256,
                            "FULL MIT LICENSE TEXT"),
                    ],
                    Scope: NoticeScope.RuntimeBundled,
                    Approval: approval,
                    Packages:
                    [
                        new NoticePackage("Pending.Package", "1.0.0"),
                    ]),
            ]);

    private const string ValidLegalFileSha256 =
        "cf3a2d0a24a2a4dde18505d43e94a24bbc0a73c8f6e1deb2bae3743c0c9370ce";
}
