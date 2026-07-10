using System.Text.Json;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Generation;

public sealed class SpdxSbomGeneratorTests
{
    [Fact]
    public void SbomPreservesDifferentDeclaredAndConcludedExpressions()
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
                        DeclaredExpression: "BSD-3-Clause OR MIT",
                        ConcludedExpression: "MIT"),
                    CopyrightNotice: "Copyright (c) Dual License Package",
                    Usage: "runtime-feature",
                    Linkage: "managed-library",
                    Modified: false,
                    SourceInformation:
                        "https://example.invalid/dual-license@commit",
                    LicenseText: "FULL SELECTED MIT LICENSE TEXT",
                    LegalFiles:
                    [
                        new VerifiedLegalFile(
                            LegalFileKind.License,
                            "licenses/dual-license/LICENSE.txt",
                            ValidLegalFileSha256,
                            "FULL SELECTED MIT LICENSE TEXT"),
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
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace: "https://example.invalid/spdx/license-decision",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");

        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.True(eligibility.IsApproved);

        var sbom = SpdxSbomGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);

        using var document = JsonDocument.Parse(sbom);
        var package = Assert.Single(document.RootElement
            .GetProperty("packages")
            .EnumerateArray());
        Assert.Equal(
            "BSD-3-Clause OR MIT",
            package.GetProperty("licenseDeclared").GetString());
        Assert.Equal(
            "MIT",
            package.GetProperty("licenseConcluded").GetString());
    }

    [Fact]
    public void PackageSpdxIdentifiersAreCollisionFreeAndOrderIndependent()
    {
        NuGetPackage[] dependencies =
        [
            new("A-B", "1.0.0", NuGetDependencyKind.Direct),
            new("A_B", "1.0.0", NuGetDependencyKind.Transitive),
        ];
        NoticeComponent[] components =
        [
            Component(
                "hyphen-package",
                "Hyphen Package",
                [new NoticePackage("A-B", "1.0.0")]),
            Component(
                "underscore-package",
                "Underscore Package",
                [new NoticePackage("A_B", "1.0.0")]),
        ];
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace: "https://example.invalid/spdx/order-test",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");

        var forward = SpdxSbomGenerator.Generate(
            context,
            dependencies,
            components);
        var reverse = SpdxSbomGenerator.Generate(
            context,
            dependencies.Reverse(),
            components.Reverse());

        Assert.Equal(forward, reverse);
        using var document = JsonDocument.Parse(forward);
        var spdxIds = document.RootElement
            .GetProperty("packages")
            .EnumerateArray()
            .Select(package => package.GetProperty("SPDXID").GetString())
            .ToArray();
        Assert.Equal(spdxIds.Length, spdxIds.Distinct().Count());
    }

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
            Component(
                "package-family",
                "Package Family",
                [
                    new NoticePackage("Direct.Package", "1.0.0"),
                    new NoticePackage("Transitive.Package", "2.0.0"),
                ]),
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

        Assert.Contains(
            "Transitive.Package@2.0.0",
            notice,
            StringComparison.Ordinal);
        Assert.Equal(
            sbom,
            SpdxSbomGenerator.Generate(context, dependencies, components));

        using var document = JsonDocument.Parse(sbom);
        var root = document.RootElement;
        Assert.Equal("SPDX-2.3", root.GetProperty("spdxVersion").GetString());
        Assert.Equal("CC0-1.0", root.GetProperty("dataLicense").GetString());
        Assert.Equal("SPDXRef-DOCUMENT", root.GetProperty("SPDXID").GetString());
        Assert.Equal("VR-Recorder-0.1.0", root.GetProperty("name").GetString());
        Assert.Equal(
            context.DocumentNamespace,
            root.GetProperty("documentNamespace").GetString());
        var creationInfo = root.GetProperty("creationInfo");
        Assert.Equal(
            "2026-07-10T00:00:00Z",
            creationInfo.GetProperty("created").GetString());
        Assert.Equal(
            context.Creator,
            creationInfo.GetProperty("creators")[0].GetString());

        var packages = root
            .GetProperty("packages")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, packages.Length);
        var packageNames = packages
            .Select(package => package.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("Direct.Package", packageNames);
        Assert.Contains("Transitive.Package", packageNames);

        var directPackage = Assert.Single(packages, package =>
            package.GetProperty("name").GetString() == "Direct.Package");
        Assert.StartsWith(
            "SPDXRef-Package-",
            directPackage.GetProperty("SPDXID").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            "1.0.0",
            directPackage.GetProperty("versionInfo").GetString());
        Assert.Equal(
            "https://example.invalid/package-family@commit",
            directPackage.GetProperty("downloadLocation").GetString());
        Assert.False(directPackage.GetProperty("filesAnalyzed").GetBoolean());
        Assert.Equal(
            "MIT",
            directPackage.GetProperty("licenseDeclared").GetString());
        Assert.Equal(
            "MIT",
            directPackage.GetProperty("licenseConcluded").GetString());
        Assert.Equal(
            "Copyright (c) Package Family",
            directPackage.GetProperty("copyrightText").GetString());
        var externalReference = Assert.Single(
            directPackage.GetProperty("externalRefs").EnumerateArray());
        Assert.Equal(
            "PACKAGE-MANAGER",
            externalReference.GetProperty("referenceCategory").GetString());
        Assert.Equal(
            "purl",
            externalReference.GetProperty("referenceType").GetString());
        Assert.Equal(
            "pkg:nuget/Direct.Package@1.0.0",
            externalReference.GetProperty("referenceLocator").GetString());

        var packageSpdxIds = packages
            .Select(package => package.GetProperty("SPDXID").GetString())
            .ToHashSet(StringComparer.Ordinal);
        var describedSpdxIds = root
            .GetProperty("documentDescribes")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(packageSpdxIds.SetEquals(describedSpdxIds));
        var relationships = root
            .GetProperty("relationships")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, relationships.Length);
        Assert.All(relationships, relationship =>
        {
            Assert.Equal(
                "SPDXRef-DOCUMENT",
                relationship.GetProperty("spdxElementId").GetString());
            Assert.Equal(
                "DESCRIBES",
                relationship.GetProperty("relationshipType").GetString());
            Assert.Contains(
                relationship.GetProperty("relatedSpdxElement").GetString(),
                packageSpdxIds);
        });
    }

    private static NoticeComponent Component(
        string id,
        string displayName,
        IReadOnlyList<NoticePackage> packages) =>
        new(
            Id: id,
            DisplayName: displayName,
            Version: "exact-version",
            LicenseExpression: "MIT",
            CopyrightNotice: $"Copyright (c) {displayName}",
            Usage: "runtime-feature",
            Linkage: "managed-library",
            Modified: false,
            SourceInformation: $"https://example.invalid/{id}@commit",
            LicenseText: "FULL MIT LICENSE TEXT",
            Scope: NoticeScope.RuntimeBundled,
            ApprovalStatus: LegalApprovalStatus.Approved,
            Packages: packages);

    private const string ValidLegalFileSha256 =
        "e7810c6fe5ed767832bcfa338323440e2c079a0fe2b6067c49b907ad2f2c1217";
}
