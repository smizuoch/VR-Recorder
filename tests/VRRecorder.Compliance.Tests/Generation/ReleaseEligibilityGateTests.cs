using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class ReleaseEligibilityGateTests
{
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

    private static NormalizedComponentGraph GraphWithApproval(
        LegalApproval approval) =>
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
                    License: new LicenseDecision("MIT", "MIT"),
                    CopyrightNotice: "Copyright (c) Example",
                    Usage: "runtime-feature",
                    Linkage: "managed-library",
                    Modified: false,
                    SourceInformation: "https://example.invalid/source@commit",
                    LicenseText: "FULL MIT LICENSE TEXT",
                    Scope: NoticeScope.RuntimeBundled,
                    Approval: approval,
                    Packages:
                    [
                        new NoticePackage("Pending.Package", "1.0.0"),
                    ]),
            ]);
}
