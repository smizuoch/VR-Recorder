using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class ThirdPartyNoticeGeneratorTests
{
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
