using VRRecorder.Compliance.Legal;

namespace VRRecorder.Compliance.Tests.Legal;

public sealed class LegalMetadataValidatorTests
{
    [Fact]
    public void ComponentWithoutCopyrightNoticeProducesIssue()
    {
        var component = new ThirdPartyComponent(
            "missing-copyright",
            "MIT",
            string.Empty,
            [new LegalFileReference("licenses/example/LICENSE.txt", ValidSha256)]);

        var issues = LegalMetadataValidator.Validate(
            [component],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["licenses/example/LICENSE.txt"] = ValidSha256,
            });

        var issue = Assert.Single(issues);
        Assert.Equal("missing-copyright-notice", issue.Code);
        Assert.Equal("missing-copyright", issue.Subject);
    }

    [Fact]
    public void ComponentWithoutLicenseFileProducesIssue()
    {
        var component = new ThirdPartyComponent(
            "missing-license",
            "MIT",
            "Copyright (c) Example",
            []);

        var issues = LegalMetadataValidator.Validate(
            [component],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var issue = Assert.Single(issues);
        Assert.Equal("missing-license-text", issue.Code);
        Assert.Equal("missing-license", issue.Subject);
    }

    [Fact]
    public void LicenseFileWithDifferentSha256ProducesIssue()
    {
        var component = new ThirdPartyComponent(
            "changed-license",
            "MIT",
            "Copyright (c) Example",
            [new LegalFileReference("licenses/example/LICENSE.txt", ValidSha256)]);
        var actualHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["licenses/example/LICENSE.txt"] = DifferentSha256,
        };

        var issues = LegalMetadataValidator.Validate([component], actualHashes);

        var issue = Assert.Single(issues);
        Assert.Equal("license-file-hash-mismatch", issue.Code);
        Assert.Equal("changed-license", issue.Subject);
    }

    private const string ValidSha256 =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string DifferentSha256 =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
}
