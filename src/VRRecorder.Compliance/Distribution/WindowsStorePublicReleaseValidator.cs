using System.Security.Cryptography;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsStorePublicReleaseValidation(
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsPublishEligible => Issues.Count == 0;
}

internal static class WindowsStorePublicReleaseValidator
{
    public static WindowsStorePublicReleaseValidation Validate(
        string packagePath,
        byte[] packagingIdentityContent,
        byte[] sideloadEvidenceContent,
        byte[] wackEvidenceContent,
        byte[] finalScanEvidenceContent,
        byte[] packagedHardwareReportContent,
        string packagedHardwareArtifactRoot,
        byte[] partnerCenterEvidenceContent,
        byte[] certificationReportContent,
        byte[] flightReportContent)
    {
        var preflight = WindowsStoreSubmissionPreflightValidator.Validate(
            packagePath,
            packagingIdentityContent,
            sideloadEvidenceContent,
            wackEvidenceContent,
            finalScanEvidenceContent,
            packagedHardwareReportContent,
            packagedHardwareArtifactRoot);
        if (!preflight.IsSubmissionReady)
        {
            return new WindowsStorePublicReleaseValidation(preflight.Issues);
        }

        WindowsStorePartnerCenterEvidence partnerCenter;
        try
        {
            partnerCenter = WindowsStorePartnerCenterEvidenceReader.Read(
                partnerCenterEvidenceContent);
        }
        catch (InvalidDataException)
        {
            return Reject(
                "partner-center-evidence-invalid",
                "partner-center-evidence");
        }

        using var package = File.OpenRead(packagePath);
        var packageSha256 = Hash(package);
        var certificationReportSha256 = Hash(certificationReportContent);
        var flightReportSha256 = Hash(flightReportContent);
        var issues = new List<ComplianceIssue>();
        if (!string.Equals(
                packageSha256,
                partnerCenter.PackageSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "partner-center-package-mismatch",
                partnerCenter.PackageSha256));
        }
        if (!string.Equals(
                certificationReportSha256,
                partnerCenter.CertificationReportSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "partner-center-certification-report-mismatch",
                certificationReportSha256));
        }
        if (!string.Equals(
                flightReportSha256,
                partnerCenter.FlightReportSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "partner-center-flight-report-mismatch",
                flightReportSha256));
        }

        var firstWackByte = wackEvidenceContent.FirstOrDefault(value =>
            value is not (byte)' ' and not (byte)'\t' and
                not (byte)'\r' and not (byte)'\n');
        if (firstWackByte == (byte)'{')
        {
            try
            {
                var waiver = WindowsAppCertificationWaiverReader.Read(
                    wackEvidenceContent);
                if (!string.Equals(
                        waiver.FlightSubmissionId,
                        partnerCenter.SubmissionId,
                        StringComparison.Ordinal))
                {
                    issues.Add(new ComplianceIssue(
                        "wack-waiver-flight-submission-mismatch",
                        waiver.FlightSubmissionId));
                }
            }
            catch (InvalidDataException)
            {
                issues.Add(new ComplianceIssue(
                    "store-wack-evidence-invalid",
                    "wack-evidence"));
            }
        }

        return new WindowsStorePublicReleaseValidation(issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray());
    }

    private static string Hash(Stream stream) => Convert
        .ToHexString(SHA256.HashData(stream))
        .ToLowerInvariant();

    private static string Hash(byte[] content) => Convert
        .ToHexString(SHA256.HashData(content))
        .ToLowerInvariant();

    private static WindowsStorePublicReleaseValidation Reject(
        string code,
        string subject) => new([new ComplianceIssue(code, subject)]);
}
