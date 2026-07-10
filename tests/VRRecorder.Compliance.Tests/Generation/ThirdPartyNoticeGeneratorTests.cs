using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class ThirdPartyNoticeGeneratorTests
{
    [Fact]
    public void ApprovedGraphNoticePreservesLicenseDecision()
    {
        var graph = new NormalizedComponentGraph(
            Dependencies:
            [
                new NuGetPackage(
                    "Dual.License.Package",
                    "3.0.0",
                    NuGetDependencyKind.Direct),
            ],
            Components:
            [
                new NormalizedComponent(
                    Id: "dual-license-package",
                    DisplayName: "Dual License Package",
                    Version: "3.0.0",
                    License: new LicenseDecision(
                        "BSD-3-Clause OR MIT",
                        "MIT"),
                    CopyrightNotice: "Copyright (c) Example",
                    Usage: "runtime-feature",
                    Linkage: "managed-library",
                    Modified: false,
                    SourceInformation:
                        "https://example.invalid/dual-license@commit",
                    LicenseText: "FULL SELECTED MIT LICENSE TEXT",
                    LegalFiles:
                    [
                        new VerifiedLegalFile(
                            LegalFileKind.Notice,
                            "notices/dual-license/NOTICE.txt",
                            ValidNoticeSha256,
                            "COMPONENT NOTICE\n\n"),
                        new VerifiedLegalFile(
                            LegalFileKind.License,
                            "licenses/dual-license/LICENSE.txt",
                            ValidLicenseSha256,
                            "SELECTED MIT LICENSE\n"),
                    ],
                    Scope: NoticeScope.RuntimeBundled,
                    Approval: new LegalApproval(
                        LegalApprovalStatus.Approved,
                        TicketId: "LEGAL-001",
                        RequestedBy: "developer",
                        Reviewer: "license-reviewer"),
                    Packages:
                    [
                        new NoticePackage("Dual.License.Package", "3.0.0"),
                    ]),
            ]);
        var eligibility = ReleaseEligibilityGate.Evaluate(graph);

        var notice = ThirdPartyNoticeGenerator.Generate(
            "VR-Recorder",
            eligibility.ApprovedGraph!);

        Assert.Contains(
            "SPDX declared: BSD-3-Clause OR MIT",
            notice,
            StringComparison.Ordinal);
        Assert.Contains(
            "SPDX concluded: MIT",
            notice,
            StringComparison.Ordinal);
        var licensePosition = notice.IndexOf(
            "--- LEGAL FILE (License): licenses/dual-license/LICENSE.txt ---",
            StringComparison.Ordinal);
        var noticePosition = notice.IndexOf(
            "--- LEGAL FILE (Notice): notices/dual-license/NOTICE.txt ---",
            StringComparison.Ordinal);
        Assert.True(licensePosition >= 0);
        Assert.True(noticePosition > licensePosition);
        Assert.Contains(
            "SELECTED MIT LICENSE\n--- END LEGAL FILE ---",
            notice,
            StringComparison.Ordinal);
        Assert.Contains(
            "COMPONENT NOTICE\n\n--- END LEGAL FILE ---",
            notice,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NoticePreservesReviewedLicenseTextWithoutTrimming()
    {
        const string reviewedLicenseText = "LINE ONE\nLINE TWO\n\n";
        NuGetPackage[] dependencies =
        [
            new("Exact.License", "1.0.0", NuGetDependencyKind.Direct),
        ];
        NoticeComponent[] components =
        [
            new(
                Id: "exact-license",
                DisplayName: "Exact License",
                Version: "1.0.0",
                LicenseExpression: "MIT",
                CopyrightNotice: "Copyright (c) Example",
                Usage: "runtime-feature",
                Linkage: "managed-library",
                Modified: false,
                SourceInformation: "https://example.invalid/source@commit",
                LicenseText: reviewedLicenseText,
                Scope: NoticeScope.RuntimeBundled,
                ApprovalStatus: LegalApprovalStatus.Approved,
                Packages: [new NoticePackage("Exact.License", "1.0.0")]),
        ];

        var notice = ThirdPartyNoticeGenerator.Generate(
            "VR-Recorder",
            dependencies,
            components);

        Assert.Contains(
            $"--- LICENSE TEXT ---\n{reviewedLicenseText}--- END LICENSE TEXT ---",
            notice,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MitComponentWithoutCopyrightNoticeStopsGeneration()
    {
        NuGetPackage[] dependencies =
        [
            new("Missing.Copyright", "1.0.0", NuGetDependencyKind.Direct),
        ];
        NoticeComponent[] components =
        [
            new(
                Id: "missing-copyright",
                DisplayName: "Missing Copyright",
                Version: "1.0.0",
                LicenseExpression: "MIT",
                CopyrightNotice: string.Empty,
                Usage: "runtime-feature",
                Linkage: "managed-library",
                Modified: false,
                SourceInformation: "https://example.invalid/source@commit",
                LicenseText: "FULL MIT LICENSE TEXT",
                Scope: NoticeScope.RuntimeBundled,
                ApprovalStatus: LegalApprovalStatus.Approved,
                Packages:
                [
                    new NoticePackage("Missing.Copyright", "1.0.0"),
                ]),
        ];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ThirdPartyNoticeGenerator.Generate(
                "VR-Recorder",
                dependencies,
                components));

        Assert.Equal(
            "Component missing-copyright is missing its copyright notice.",
            exception.Message);
    }

    [Fact]
    public void DirectDependencyIsAddedWithFullLegalMetadataAndLicenseText()
    {
        NuGetPackage[] dependencies =
        [
            new("Direct.Package", "1.0.0", NuGetDependencyKind.Direct),
        ];
        NoticeComponent[] components =
        [
            new(
                Id: "direct-package",
                DisplayName: "Direct Package",
                Version: "1.0.0",
                LicenseExpression: "MIT",
                CopyrightNotice: "Copyright (c) Example",
                Usage: "runtime-feature",
                Linkage: "managed-library",
                Modified: false,
                SourceInformation: "https://example.invalid/source@commit",
                LicenseText: "FULL MIT LICENSE TEXT",
                Scope: NoticeScope.RuntimeBundled,
                ApprovalStatus: LegalApprovalStatus.Approved,
                Packages: [new NoticePackage("Direct.Package", "1.0.0")]),
        ];

        var notice = ThirdPartyNoticeGenerator.Generate(
            "VR-Recorder",
            dependencies,
            components);

        Assert.Contains("Direct Package", notice, StringComparison.Ordinal);
        Assert.Contains("Version: 1.0.0", notice, StringComparison.Ordinal);
        Assert.Contains("SPDX: MIT", notice, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) Example", notice, StringComparison.Ordinal);
        Assert.Contains("Usage: runtime-feature", notice, StringComparison.Ordinal);
        Assert.Contains("Linkage: managed-library", notice, StringComparison.Ordinal);
        Assert.Contains("Modified: no", notice, StringComparison.Ordinal);
        Assert.Contains("FULL MIT LICENSE TEXT", notice, StringComparison.Ordinal);
    }

    private const string ValidLicenseSha256 =
        "a5019fdc5b3c42dc39c743d45f66d814fca4c0042f959acc84a650f5139bcde1";

    private const string ValidNoticeSha256 =
        "73956b3a208cf4f35d1b0d9982295204f9d1d48d17d0432cf747573cf4cfb148";
}
