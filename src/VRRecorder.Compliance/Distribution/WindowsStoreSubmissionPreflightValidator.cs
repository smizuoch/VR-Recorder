using System.Security.Cryptography;
using System.Text.Json;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsStoreSubmissionPreflightResult(
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsSubmissionReady => Issues.Count == 0;
}

internal static class WindowsStoreSubmissionPreflightValidator
{
    public static WindowsStoreSubmissionPreflightResult Validate(
        string packagePath,
        byte[] packagingIdentityContent,
        byte[] sideloadEvidenceContent,
        byte[] wackEvidenceContent,
        byte[] finalScanEvidenceContent,
        byte[] packagedHardwareReportContent,
        string packagedHardwareArtifactRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(packagingIdentityContent);
        ArgumentNullException.ThrowIfNull(sideloadEvidenceContent);
        ArgumentNullException.ThrowIfNull(wackEvidenceContent);
        ArgumentNullException.ThrowIfNull(finalScanEvidenceContent);
        ArgumentNullException.ThrowIfNull(packagedHardwareReportContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            packagedHardwareArtifactRoot);
        if (!File.Exists(packagePath))
        {
            return Reject("store-package-missing", packagePath);
        }

        StorePackagingAnchor packaging;
        WindowsStoreSideloadEvidence sideload;
        WindowsAppCertificationReport? wack = null;
        WindowsAppCertificationWaiver? wackWaiver = null;
        WindowsStoreFinalScanEvidence finalScan;
        try
        {
            packaging = ReadPackagingAnchor(packagingIdentityContent);
        }
        catch (InvalidDataException)
        {
            return Reject(
                "store-packaging-identity-invalid",
                "packaging-identity");
        }

        try
        {
            sideload = WindowsStoreSideloadEvidenceReader.Read(
                sideloadEvidenceContent);
        }
        catch (InvalidDataException)
        {
            return Reject(
                "store-sideload-evidence-invalid",
                "sideload-evidence");
        }

        var firstWackByte = wackEvidenceContent.FirstOrDefault(value =>
            value is not (byte)' ' and not (byte)'\t' and
                not (byte)'\r' and not (byte)'\n');
        try
        {
            if (firstWackByte == (byte)'<')
            {
                wack = WindowsAppCertificationReportReader.Read(
                    wackEvidenceContent);
            }
            else if (firstWackByte == (byte)'{')
            {
                wackWaiver = WindowsAppCertificationWaiverReader.Read(
                    wackEvidenceContent);
            }
            else
            {
                throw new InvalidDataException();
            }
        }
        catch (InvalidDataException)
        {
            return Reject("store-wack-evidence-invalid", "wack-evidence");
        }

        try
        {
            finalScan = WindowsStoreFinalScanEvidenceReader.Read(
                finalScanEvidenceContent);
        }
        catch (InvalidDataException)
        {
            return Reject(
                "store-final-scan-evidence-invalid",
                "final-scan-evidence");
        }

        using var package = File.OpenRead(packagePath);
        var actualPackageSha256 = Convert
            .ToHexString(SHA256.HashData(package))
            .ToLowerInvariant();
        var issues = new List<ComplianceIssue>();
        if (!string.Equals(
                Path.GetFileName(packagePath),
                packaging.PackageFileName,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "store-package-file-name-mismatch",
                Path.GetFileName(packagePath)));
        }

        if (!string.Equals(
                actualPackageSha256,
                packaging.PackageSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                actualPackageSha256,
                sideload.PackageSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                actualPackageSha256,
                finalScan.PackageSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "store-package-sha256-mismatch",
                actualPackageSha256));
        }

        if (!string.Equals(
                packaging.Publisher,
                sideload.ManifestPublisher,
                StringComparison.Ordinal) ||
            !string.Equals(
                packaging.Publisher,
                sideload.CertificateSubject,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "store-sideload-certificate-subject-mismatch",
                sideload.CertificateSubject));
        }

        AddFailedCheck(issues, sideload.SignatureVerified, "signature-verify");
        AddFailedCheck(issues, sideload.InstallSucceeded, "install");
        AddFailedCheck(issues, sideload.LaunchSucceeded, "launch");
        AddFailedCheck(issues, sideload.UninstallSucceeded, "uninstall");
        AddFailedCheck(
            issues,
            sideload.InstallRootReadOnly,
            "install-root-read-only");
        AddFailedCheck(
            issues,
            sideload.WorkingDirectoryIndependent,
            "working-directory-independent");
        AddFailedCheck(issues, sideload.SettingsPassed, "settings");
        AddFailedCheck(issues, sideload.DiagnosticsPassed, "diagnostics");
        AddFailedCheck(issues, sideload.LegalDisplayPassed, "legal-display");
        if (wack is not null && !wack.IsPassed)
        {
            issues.Add(new ComplianceIssue(
                "store-wack-not-passed",
                string.Join(',', wack.NonPassingResults)));
        }
        if (wackWaiver is not null && !string.Equals(
                actualPackageSha256,
                wackWaiver.PackageSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "store-wack-waiver-package-mismatch",
                wackWaiver.PackageSha256));
        }
        var packagedHardware =
            WindowsStorePackagedHardwareEvidenceValidator.Validate(
                packagedHardwareReportContent,
                packagedHardwareArtifactRoot,
                actualPackageSha256);
        issues.AddRange(packagedHardware.Issues);
        if (!string.Equals(
                packaging.LegalManifestSha256,
                finalScan.LegalManifestSha256,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "store-final-scan-legal-manifest-mismatch",
                finalScan.LegalManifestSha256));
        }
        AddFailedFinalScanCheck(
            issues,
            finalScan.MalwareScanPassed,
            "malware-scan");
        AddFailedFinalScanCheck(
            issues,
            finalScan.LegalBundleVerified,
            "legal-bundle");
        AddFailedFinalScanCheck(
            issues,
            finalScan.SbomPresent,
            "sbom");
        AddFailedFinalScanCheck(
            issues,
            finalScan.PrivateKeysAbsent,
            "private-keys");

        return new WindowsStoreSubmissionPreflightResult(Order(issues));
    }

    private static StorePackagingAnchor ReadPackagingAnchor(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("candidateKind").GetString() !=
                "microsoft-store-packaging-candidate" ||
                root.GetProperty("publishEligible").ValueKind !=
                JsonValueKind.False)
            {
                throw new InvalidDataException();
            }

            var fileName = RequiredString(root, "packageFileName");
            var sha256 = RequiredString(root, "packageSha256");
            var publisher = RequiredString(
                root.GetProperty("storeIdentity"),
                "publisher");
            var legalManifestSha256 = RequiredString(
                root.GetProperty("validatedPayload"),
                "legalManifestSha256");
            if (Path.GetFileName(fileName) != fileName ||
                !fileName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                !IsLowerHexSha256(sha256) ||
                !IsLowerHexSha256(legalManifestSha256))
            {
                throw new InvalidDataException();
            }

            return new StorePackagingAnchor(
                fileName,
                sha256,
                publisher,
                legalManifestSha256);
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidOperationException or
            KeyNotFoundException or ArgumentException)
        {
            throw new InvalidDataException(
                "The Store packaging identity is invalid.",
                exception);
        }
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException()
            : value;
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void AddFailedCheck(
        List<ComplianceIssue> issues,
        bool passed,
        string subject)
    {
        if (!passed)
        {
            issues.Add(new ComplianceIssue(
                "store-sideload-check-failed",
                subject));
        }
    }

    private static void AddFailedFinalScanCheck(
        List<ComplianceIssue> issues,
        bool passed,
        string subject)
    {
        if (!passed)
        {
            issues.Add(new ComplianceIssue(
                "store-final-scan-check-failed",
                subject));
        }
    }

    private static WindowsStoreSubmissionPreflightResult Reject(
        string code,
        string subject) => new([new ComplianceIssue(code, subject)]);

    private static ComplianceIssue[] Order(
        IEnumerable<ComplianceIssue> issues) => issues
        .OrderBy(issue => issue.Code, StringComparer.Ordinal)
        .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
        .ToArray();

    private sealed record StorePackagingAnchor(
        string PackageFileName,
        string PackageSha256,
        string Publisher,
        string LegalManifestSha256);
}
