namespace VRRecorder.Compliance.Distribution;

internal static class DistributionPromotionPolicy
{
    public static DistributionPromotionResult Evaluate(
        DistributionPromotionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var issues = ValidateCommonRequest(request).ToList();
        switch (request.Target)
        {
            case DistributionTarget.HardwareValidationPayload:
                ValidateHardwareValidationExecutable(request, issues);
                break;
            case DistributionTarget.MicrosoftStorePackagingCandidate:
                ValidateMicrosoftStoreMsix(request, issues);
                break;
            default:
                issues.Add(new ComplianceIssue(
                    "unsupported-distribution-target",
                    request.Target.ToString()));
                break;
        }

        var orderedIssues = issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
        var allowed = orderedIssues.Length == 0;
        return new DistributionPromotionResult(
            allowed,
            PublishEligible: false,
            orderedIssues);
    }

    private static IEnumerable<ComplianceIssue> ValidateCommonRequest(
        DistributionPromotionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ArtifactPath))
        {
            yield return new ComplianceIssue(
                "distribution-artifact-path-required",
                string.Empty);
        }

        if (string.IsNullOrWhiteSpace(request.Payload.ProductVersion))
        {
            yield return new ComplianceIssue(
                "distribution-product-version-required",
                string.Empty);
        }

        if (!IsCanonicalSourceRevision(request.Payload.SourceRevision))
        {
            yield return new ComplianceIssue(
                "distribution-source-revision-invalid",
                request.Payload.SourceRevision ?? string.Empty);
        }

        if (!string.Equals(
                request.Payload.RuntimeIdentifier,
                "win-x64",
                StringComparison.Ordinal))
        {
            yield return new ComplianceIssue(
                "hardware-validation-runtime-identifier-invalid",
                request.Payload.RuntimeIdentifier ?? string.Empty);
        }

        if (!IsCanonicalSha256(
                request.Payload.ApplicationExecutableSha256))
        {
            yield return new ComplianceIssue(
                "application-executable-sha256-invalid",
                request.Payload.ApplicationExecutableSha256 ?? string.Empty);
        }

        if (!IsCanonicalSha256(request.Payload.PayloadInventorySha256))
        {
            yield return new ComplianceIssue(
                "payload-inventory-sha256-invalid",
                request.Payload.PayloadInventorySha256 ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(request.Payload.LegalBundleId))
        {
            yield return new ComplianceIssue(
                "legal-bundle-id-required",
                string.Empty);
        }

        if (!IsCanonicalSha256(request.Payload.LegalManifestSha256))
        {
            yield return new ComplianceIssue(
                "legal-manifest-sha256-invalid",
                request.Payload.LegalManifestSha256 ?? string.Empty);
        }
    }

    private static void ValidateHardwareValidationExecutable(
        DistributionPromotionRequest request,
        List<ComplianceIssue> issues)
    {
        if (!HasExtension(request.ArtifactPath, ".exe"))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-requires-exe",
                request.ArtifactPath));
        }
    }

    private static void ValidateMicrosoftStoreMsix(
        DistributionPromotionRequest request,
        List<ComplianceIssue> issues)
    {
        if (!HasExtension(request.ArtifactPath, ".msix") &&
            !HasExtension(request.ArtifactPath, ".msixupload"))
        {
            issues.Add(new ComplianceIssue(
                "microsoft-store-requires-msix",
                request.ArtifactPath));
        }

        ValidateStoreIdentity(request, issues);
        ValidateHardwareEvidence(request, issues);
    }

    private static void ValidateStoreIdentity(
        DistributionPromotionRequest request,
        List<ComplianceIssue> issues)
    {
        if (request.StoreIdentity is null)
        {
            issues.Add(new ComplianceIssue(
                "microsoft-store-identity-required",
                request.ArtifactPath));
            return;
        }

        if (IsPlaceholderIdentityValue(request.StoreIdentity.Name) ||
            IsPlaceholderIdentityValue(request.StoreIdentity.Publisher) ||
            IsPlaceholderIdentityValue(
                request.StoreIdentity.PublisherDisplayName) ||
            string.Equals(
                request.StoreIdentity.Publisher,
                "CN=00000000-0000-0000-0000-000000000000",
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ComplianceIssue(
                "microsoft-store-identity-invalid",
                request.ArtifactPath));
        }
    }

    private static void ValidateHardwareEvidence(
        DistributionPromotionRequest request,
        List<ComplianceIssue> issues)
    {
        var evidence = request.HardwareValidation;
        if (evidence is null)
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-required",
                request.Payload.SourceRevision));
            return;
        }

        ArgumentNullException.ThrowIfNull(evidence.Payload);

        if (!string.Equals(
                evidence.Payload.ProductVersion,
                request.Payload.ProductVersion,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-version-mismatch",
                evidence.Payload.ProductVersion));
        }

        if (!string.Equals(
                evidence.Payload.SourceRevision,
                request.Payload.SourceRevision,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-revision-mismatch",
                evidence.Payload.SourceRevision));
        }

        if (!string.Equals(
                evidence.Payload.ApplicationExecutableSha256,
                request.Payload.ApplicationExecutableSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-executable-mismatch",
                evidence.Payload.ApplicationExecutableSha256));
        }

        if (!string.Equals(
                evidence.Payload.PayloadInventorySha256,
                request.Payload.PayloadInventorySha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-payload-inventory-mismatch",
                evidence.Payload.PayloadInventorySha256));
        }

        if (!string.Equals(
                evidence.Payload.LegalBundleId,
                request.Payload.LegalBundleId,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-legal-bundle-mismatch",
                evidence.Payload.LegalBundleId));
        }

        if (!string.Equals(
                evidence.Payload.LegalManifestSha256,
                request.Payload.LegalManifestSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-legal-manifest-mismatch",
                evidence.Payload.LegalManifestSha256));
        }

        if (!IsCanonicalSha256(evidence.ValidationReportSha256))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-report-sha256-invalid",
                evidence.ValidationReportSha256));
        }
    }

    private static bool HasExtension(string path, string extension) =>
        string.Equals(
            Path.GetExtension(path),
            extension,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsCanonicalSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsCanonicalSourceRevision(string? value) =>
        value is { Length: 40 or 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsPlaceholderIdentityValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains('<', StringComparison.Ordinal) ||
        value.Contains('>', StringComparison.Ordinal) ||
        value.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("example", StringComparison.OrdinalIgnoreCase);
}
