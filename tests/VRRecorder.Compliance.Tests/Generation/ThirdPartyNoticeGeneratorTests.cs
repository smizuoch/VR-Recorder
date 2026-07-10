using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class ThirdPartyNoticeGeneratorTests
{
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
}
