using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsHardwareValidationEvidenceValidation(
    HardwareValidationEvidence? Evidence,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsValidated => Evidence is not null && Issues.Count == 0;
}

internal static class WindowsHardwareValidationEvidenceValidator
{
    public static async Task<WindowsHardwareValidationEvidenceValidation>
        ValidateAsync(
            byte[] payloadIdentityContent,
            byte[] reportContent,
            string artifactRoot,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloadIdentityContent);
        ArgumentNullException.ThrowIfNull(reportContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        cancellationToken.ThrowIfCancellationRequested();

        WindowsApplicationPayloadIdentityDocument identity;
        try
        {
            identity = WindowsApplicationPayloadIdentityReader.Read(
                payloadIdentityContent);
        }
        catch (InvalidDataException)
        {
            return Reject(
                "hardware-validation-payload-identity-invalid",
                "payload-identity");
        }

        WindowsHardwareValidationReport report;
        try
        {
            report = WindowsHardwareValidationReportReader.Read(reportContent);
        }
        catch (InvalidDataException)
        {
            return Reject(
                "hardware-validation-report-invalid",
                "hardware-validation-report");
        }

        var issues = new List<ComplianceIssue>();
        if (report.PayloadIdentitySha256 != identity.DocumentSha256)
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-payload-identity-mismatch",
                report.PayloadIdentitySha256));
        }

        ValidateCases(report, issues);
        ValidateMatrix(report, issues);
        await ValidateArtifactsAsync(
                report,
                artifactRoot,
                issues,
                cancellationToken)
            .ConfigureAwait(false);

        if (issues.Count != 0)
        {
            return Reject(issues);
        }

        return new WindowsHardwareValidationEvidenceValidation(
            new HardwareValidationEvidence(
                identity.Payload,
                identity.DocumentSha256,
                report.ReportSha256,
                report.Runs.Select(run => run.RunId).ToArray()),
            []);
    }

    private static void ValidateCases(
        WindowsHardwareValidationReport report,
        List<ComplianceIssue> issues)
    {
        var allCases = report.Runs
            .SelectMany(run => run.Cases.Select(testCase => (run, testCase)))
            .ToArray();
        var caseIds = allCases
            .Select(item => item.testCase.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in WindowsHardwareValidationMatrixProfile
                     .RequiredCaseIds)
        {
            if (!caseIds.Contains(required))
            {
                issues.Add(new ComplianceIssue(
                    "hardware-validation-required-case-missing",
                    required));
            }
        }

        foreach (var (run, testCase) in allCases)
        {
            var subject = $"{run.RunId:D}/{testCase.Id}";
            switch (testCase.Status)
            {
                case HardwareValidationCaseStatus.Fail:
                    issues.Add(new ComplianceIssue(
                        "hardware-validation-case-failed",
                        subject));
                    break;
                case HardwareValidationCaseStatus.Skip:
                    issues.Add(new ComplianceIssue(
                        "hardware-validation-case-skipped",
                        subject));
                    break;
                case HardwareValidationCaseStatus.Pass:
                    break;
                default:
                    throw new InvalidOperationException(
                        "The parsed report contains an unknown case status.");
            }
        }
    }

    private static void ValidateMatrix(
        WindowsHardwareValidationReport report,
        List<ComplianceIssue> issues)
    {
        foreach (var profile in new[] { "windows-10-22h2", "windows-11" })
        {
            if (report.Runs.All(run =>
                    run.Environment.OperatingSystem.Profile != profile))
            {
                issues.Add(new ComplianceIssue(
                    "hardware-validation-os-profile-missing",
                    profile));
            }
        }

        RequireHardwareEncoder(
            report,
            "nvidia",
            HardwareEncoderApi.Nvenc,
            issues);
        RequireHardwareEncoder(
            report,
            "amd",
            HardwareEncoderApi.Amf,
            issues);
        RequireHardwareEncoder(
            report,
            "intel",
            HardwareEncoderApi.Qsv,
            issues);
        if (report.Runs.All(run =>
                run.Environment.Encoder.Mode !=
                    HardwareEncoderMode.Software ||
                run.Environment.Encoder.Api !=
                    HardwareEncoderApi.MediaFoundation))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-software-fallback-missing",
                "media-foundation"));
        }

        if (report.Runs.All(run =>
                !run.Environment.SteamVr.IsConnected ||
                run.Cases.All(testCase =>
                    testCase.Id != "openvr-overlay-controller")))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-steamvr-controller-missing",
                "openvr-overlay-controller"));
        }
    }

    private static void RequireHardwareEncoder(
        WindowsHardwareValidationReport report,
        string vendor,
        HardwareEncoderApi api,
        List<ComplianceIssue> issues)
    {
        if (report.Runs.All(run =>
                run.Environment.Gpu.Vendor != vendor ||
                run.Environment.Encoder.Mode != HardwareEncoderMode.Hardware ||
                run.Environment.Encoder.Api != api))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-hardware-encoder-matrix-missing",
                vendor));
        }
    }

    private static async Task ValidateArtifactsAsync(
        WindowsHardwareValidationReport report,
        string artifactRoot,
        List<ComplianceIssue> issues,
        CancellationToken cancellationToken)
    {
        string root;
        try
        {
            root = Path.GetFullPath(artifactRoot);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException)
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-artifact-root-invalid",
                artifactRoot));
            return;
        }

        if (!Path.IsPathFullyQualified(artifactRoot) ||
            !string.Equals(root, artifactRoot, PathComparison) ||
            !Directory.Exists(root) ||
            !RepositoryEvidenceRoot.TryResolve(root, out var canonicalRoot) ||
            !string.Equals(root, canonicalRoot, PathComparison))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-artifact-root-invalid",
                artifactRoot));
            return;
        }

        StagingInventory scanned;
        try
        {
            scanned = await new FileSystemStagingInventoryReader()
                .ReadAsync(root, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-artifact-root-invalid",
                root));
            return;
        }

        foreach (var scanIssue in scanned.ScanIssues)
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-artifact-semantics-invalid",
                scanIssue.Subject));
        }

        var expected = report.Runs
            .SelectMany(run => run.Cases)
            .SelectMany(testCase => testCase.Artifacts)
            .ToArray();
        var expectedPaths = expected.Select(artifact => artifact.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = scanned.Files.Where(file => string.Equals(
                    file.RelativePath,
                    artifact.Path,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "hardware-validation-artifact-missing",
                    artifact.Path));
                continue;
            }

            if (matches.Length != 1 ||
                matches[0].RelativePath != artifact.Path ||
                matches[0].Length != artifact.Length ||
                matches[0].Sha256 != artifact.Sha256)
            {
                issues.Add(new ComplianceIssue(
                    "hardware-validation-artifact-mismatch",
                    artifact.Path));
                continue;
            }

            try
            {
                WindowsRuntimeFileSemanticsVerifier.Instance.VerifyRegularFile(
                    WindowsRuntimeRelativePath.Resolve(root, artifact.Path));
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException)
            {
                issues.Add(new ComplianceIssue(
                    "hardware-validation-artifact-semantics-invalid",
                    artifact.Path));
            }
        }

        foreach (var unexpected in scanned.Files.Where(file =>
                     !expectedPaths.Contains(file.RelativePath)))
        {
            issues.Add(new ComplianceIssue(
                "hardware-validation-artifact-unexpected",
                unexpected.RelativePath));
        }
    }

    private static WindowsHardwareValidationEvidenceValidation Reject(
        string code,
        string subject) => Reject([new ComplianceIssue(code, subject)]);

    private static WindowsHardwareValidationEvidenceValidation Reject(
        IEnumerable<ComplianceIssue> issues) => new(
        null,
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray());

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
