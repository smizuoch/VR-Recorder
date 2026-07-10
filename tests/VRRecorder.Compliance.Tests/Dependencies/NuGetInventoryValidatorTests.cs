using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Tests.Dependencies;

public sealed class NuGetInventoryValidatorTests
{
    [Fact]
    public void UnregisteredTransitivePackageProducesFailClosedIssue()
    {
        NuGetPackage[] packages =
        [
            new("Direct.Package", "1.0.0", NuGetDependencyKind.Direct),
            new("Transitive.Package", "2.0.0", NuGetDependencyKind.Transitive),
        ];
        RegisteredNuGetPackage[] registry =
        [
            new("Direct.Package", "1.0.0"),
        ];

        var issues = NuGetInventoryValidator.Validate(packages, registry);

        var issue = Assert.Single(issues);
        Assert.Equal("missing-component-registration", issue.Code);
        Assert.Equal("Transitive.Package@2.0.0", issue.Subject);
    }

    [Fact]
    public void RegisteredPackageAtDifferentVersionProducesMismatchIssue()
    {
        NuGetPackage[] packages =
        [
            new("Versioned.Package", "2.0.0", NuGetDependencyKind.Direct),
        ];
        RegisteredNuGetPackage[] registry =
        [
            new("Versioned.Package", "1.0.0"),
        ];

        var issues = NuGetInventoryValidator.Validate(packages, registry);

        var issue = Assert.Single(issues);
        Assert.Equal("registry-version-mismatch", issue.Code);
        Assert.Equal("Versioned.Package@2.0.0", issue.Subject);
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("NOASSERTION")]
    [InlineData("NONE")]
    public void PackageWithUnacceptableLicenseConclusionProducesIssue(string license)
    {
        NuGetPackage[] packages =
        [
            new("Forbidden.Package", "1.0.0", NuGetDependencyKind.Direct),
        ];
        RegisteredNuGetPackage[] registry =
        [
            new("Forbidden.Package", "1.0.0", license),
        ];

        var issues = NuGetInventoryValidator.Validate(packages, registry);

        var issue = Assert.Single(issues);
        Assert.Equal("unacceptable-license-conclusion", issue.Code);
        Assert.Equal("Forbidden.Package@1.0.0", issue.Subject);
    }
}
