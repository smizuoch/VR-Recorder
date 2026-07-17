using VRRecorder.Compliance.Distribution;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class DistributionPromotionPolicyTests
{
    private const string ProductVersion = "0.1.0";
    private const string SourceRevision =
        "0123456789abcdef0123456789abcdef01234567";
    private const string ExecutableSha256 =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ReportSha256 =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string PayloadInventorySha256 =
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string LegalBundleId =
        "urn:vr-recorder:legal:0.1.0:0123456789abcdef";
    private const string LegalManifestSha256 =
        "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    [Fact]
    public void LowLevelPromotionPolicyIsNotAPublicReleaseSurface()
    {
        Assert.False(typeof(DistributionPromotionPolicy).IsPublic);
        Assert.False(typeof(HardwareValidationEvidence).IsPublic);
    }

    [Fact]
    public void UnpackagedExecutableIsAllowedOnlyForHardwareValidation()
    {
        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.HardwareValidationPayload,
                "VR-Recorder.exe"));

        Assert.True(result.Allowed);
        Assert.False(result.PublishEligible);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void HardwareValidationTargetRejectsPackagedArtifact()
    {
        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.HardwareValidationPayload,
                "VR-Recorder.msix"));

        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("hardware-validation-requires-exe", issue.Code);
        Assert.Equal("VR-Recorder.msix", issue.Subject);
    }

    [Theory]
    [InlineData("VR-Recorder.msix")]
    [InlineData("VR-Recorder.msixupload")]
    public void MatchingPassedHardwareEvidenceAllowsStoreMsixCandidateBuild(
        string artifactPath)
    {
        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                artifactPath,
                HardwareEvidence(),
                StoreIdentity()));

        Assert.True(result.Allowed);
        Assert.False(result.PublishEligible);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void StoreMsixWithoutHardwareEvidenceIsRejected()
    {
        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                "VR-Recorder.msix",
                hardwareValidation: null,
                StoreIdentity()));

        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("hardware-validation-required", issue.Code);
        Assert.Equal(SourceRevision, issue.Subject);
    }

    [Fact]
    public void HardwareEvidenceHasNoCallerSuppliedPassedFlag()
    {
        Assert.Null(typeof(HardwareValidationEvidence).GetProperty("Passed"));
    }

    [Theory]
    [InlineData("0.1.1", SourceRevision, ExecutableSha256,
        "hardware-validation-version-mismatch")]
    [InlineData(ProductVersion,
        "fedcba9876543210fedcba9876543210fedcba98",
        ExecutableSha256,
        "hardware-validation-revision-mismatch")]
    [InlineData(ProductVersion, SourceRevision,
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
        "hardware-validation-executable-mismatch")]
    public void StoreMsixMustContainTheValidatedExecutableBuild(
        string validatedVersion,
        string validatedRevision,
        string validatedExecutableSha256,
        string expectedCode)
    {
        var evidence = HardwareEvidence();
        evidence = evidence with
        {
            Payload = evidence.Payload with
            {
                ProductVersion = validatedVersion,
                SourceRevision = validatedRevision,
                ApplicationExecutableSha256 = validatedExecutableSha256,
            },
        };

        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                "VR-Recorder.msix",
                evidence,
                StoreIdentity()));

        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
    }

    [Fact]
    public void StoreMsixMustContainTheValidatedPublishDirectoryPayload()
    {
        var evidence = HardwareEvidence();
        evidence = evidence with
        {
            Payload = evidence.Payload with
            {
                PayloadInventorySha256 =
                    "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            },
        };

        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                "VR-Recorder.msix",
                evidence,
                StoreIdentity()));

        Assert.Contains(
            result.Issues,
            issue => issue.Code ==
                "hardware-validation-payload-inventory-mismatch");
        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
    }

    [Theory]
    [InlineData("different-bundle", LegalManifestSha256,
        "hardware-validation-legal-bundle-mismatch")]
    [InlineData(LegalBundleId,
        "9999999999999999999999999999999999999999999999999999999999999999",
        "hardware-validation-legal-manifest-mismatch")]
    public void StoreMsixMustCarryTheValidatedLegalBundle(
        string validatedBundleId,
        string validatedManifestSha256,
        string expectedCode)
    {
        var evidence = HardwareEvidence();
        evidence = evidence with
        {
            Payload = evidence.Payload with
            {
                LegalBundleId = validatedBundleId,
                LegalManifestSha256 = validatedManifestSha256,
            },
        };

        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                "VR-Recorder.msix",
                evidence,
                StoreIdentity()));

        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
    }

    [Fact]
    public void StoreMsixRequiresPartnerCenterIdentity()
    {
        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                "VR-Recorder.msix",
                HardwareEvidence(),
                storeIdentity: null));

        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("microsoft-store-identity-required", issue.Code);
        Assert.Equal("VR-Recorder.msix", issue.Subject);
    }

    [Theory]
    [InlineData("<identity-name>",
        "CN=12345678-1234-1234-1234-123456789abc",
        "VR Recorder Project")]
    [InlineData("VRRecorder.Project",
        "CN=00000000-0000-0000-0000-000000000000",
        "VR Recorder Project")]
    [InlineData("VRRecorder.Project",
        "CN=12345678-1234-1234-1234-123456789abc",
        "<publisher-display-name>")]
    public void StoreCandidateRejectsPlaceholderPartnerCenterIdentity(
        string name,
        string publisher,
        string publisherDisplayName)
    {
        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                "VR-Recorder.msix",
                HardwareEvidence(),
                new MicrosoftStoreIdentity(
                    name,
                    publisher,
                    publisherDisplayName)));

        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("microsoft-store-identity-invalid", issue.Code);
        Assert.Equal("VR-Recorder.msix", issue.Subject);
    }

    [Fact]
    public void StoreTargetRejectsAnExecutableArtifact()
    {
        var result = DistributionPromotionPolicy.Evaluate(
            CreateRequest(
                DistributionTarget.MicrosoftStorePackagingCandidate,
                "VR-Recorder.exe",
                HardwareEvidence(),
                StoreIdentity()));

        Assert.Contains(
            result.Issues,
            issue => issue.Code == "microsoft-store-requires-msix");
        Assert.False(result.Allowed);
        Assert.False(result.PublishEligible);
    }

    private static DistributionPromotionRequest CreateRequest(
        DistributionTarget target,
        string artifactPath,
        HardwareValidationEvidence? hardwareValidation = null,
        MicrosoftStoreIdentity? storeIdentity = null) =>
        new(
            target,
            artifactPath,
            PayloadIdentity(),
            hardwareValidation,
            storeIdentity);

    private static HardwareValidationEvidence HardwareEvidence() =>
        new(
            PayloadIdentity(),
            ReportSha256,
            ReportSha256,
            [Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab")]);

    private static ValidatedPayloadIdentity PayloadIdentity() =>
        new(
            ProductVersion,
            SourceRevision,
            "win-x64",
            ExecutableSha256,
            PayloadInventorySha256,
            LegalBundleId,
            LegalManifestSha256);

    private static MicrosoftStoreIdentity StoreIdentity() =>
        new(
            "VRRecorder.Project",
            "CN=12345678-1234-1234-1234-123456789abc",
            "VR Recorder Project");
}
