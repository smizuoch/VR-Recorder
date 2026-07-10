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
}
