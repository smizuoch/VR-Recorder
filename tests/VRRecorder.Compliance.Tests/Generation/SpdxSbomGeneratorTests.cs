using System.Text.Json;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class SpdxSbomGeneratorTests
{
    [Fact]
    public void TransitiveDependencyIsAddedToNoticeAndSbom()
    {
        NuGetPackage[] dependencies =
        [
            new("Direct.Package", "1.0.0", NuGetDependencyKind.Direct),
            new("Transitive.Package", "2.0.0", NuGetDependencyKind.Transitive),
        ];
        NoticeComponent[] components =
        [
            Component("direct", "Direct Package", "Direct.Package", "1.0.0"),
            Component(
                "transitive",
                "Transitive Package",
                "Transitive.Package",
                "2.0.0"),
        ];
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace: "https://example.invalid/spdx/vr-recorder/0.1.0",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");

        var notice = ThirdPartyNoticeGenerator.Generate(
            context.ProductName,
            dependencies,
            components);
        var sbom = SpdxSbomGenerator.Generate(context, dependencies, components);

        Assert.Contains("Transitive Package", notice, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(sbom);
        var packageNames = document.RootElement
            .GetProperty("packages")
            .EnumerateArray()
            .Select(package => package.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("Direct Package", packageNames);
        Assert.Contains("Transitive Package", packageNames);
    }

    private static NoticeComponent Component(
        string id,
        string displayName,
        string packageId,
        string version) =>
        new(
            Id: id,
            DisplayName: displayName,
            Version: version,
            LicenseExpression: "MIT",
            CopyrightNotice: $"Copyright (c) {displayName}",
            Usage: "runtime-feature",
            Linkage: "managed-library",
            Modified: false,
            SourceInformation: $"https://example.invalid/{id}@commit",
            LicenseText: "FULL MIT LICENSE TEXT",
            Scope: NoticeScope.RuntimeBundled,
            ApprovalStatus: LegalApprovalStatus.Approved,
            Packages: [new NoticePackage(packageId, version)]);
}
