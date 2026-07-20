using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Distribution;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsStoreSubmissionPreflightValidatorTests
{
    [Fact]
    public void ExactPackageAndAllMachineChecksAreSubmissionReady()
    {
        using var fixture = new Fixture();

        var result = WindowsStoreSubmissionPreflightValidator.Validate(
            fixture.PackagePath,
            fixture.PackagingIdentity(),
            fixture.SideloadEvidence(),
            Wack("PASS"),
            fixture.FinalScanEvidence(),
            fixture.PackagedHardwareReport(),
            fixture.HardwareArtifactRoot);

        Assert.True(result.IsSubmissionReady);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DifferentCertificateSubjectIsRejected()
    {
        using var fixture = new Fixture();

        var result = WindowsStoreSubmissionPreflightValidator.Validate(
            fixture.PackagePath,
            fixture.PackagingIdentity(),
            fixture.SideloadEvidence(certificateSubject: "CN=other"),
            Wack("PASS"),
            fixture.FinalScanEvidence(),
            fixture.PackagedHardwareReport(),
            fixture.HardwareArtifactRoot);

        Assert.False(result.IsSubmissionReady);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "store-sideload-certificate-subject-mismatch");
    }

    [Fact]
    public void AnyMissingLifecycleCheckAndWackFailureAreRejected()
    {
        using var fixture = new Fixture();

        var result = WindowsStoreSubmissionPreflightValidator.Validate(
            fixture.PackagePath,
            fixture.PackagingIdentity(),
            fixture.SideloadEvidence(settingsPassed: false),
            Wack("FAIL"),
            fixture.FinalScanEvidence(),
            fixture.PackagedHardwareReport(),
            fixture.HardwareArtifactRoot);

        Assert.False(result.IsSubmissionReady);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "store-sideload-check-failed" &&
            issue.Subject == "settings");
        Assert.Contains(result.Issues, issue =>
            issue.Code == "store-wack-not-passed");
    }

    [Fact]
    public void PackageTamperAfterEvidenceIsRejected()
    {
        using var fixture = new Fixture();
        var identity = fixture.PackagingIdentity();
        var sideload = fixture.SideloadEvidence();
        File.AppendAllText(fixture.PackagePath, "tamper");

        var result = WindowsStoreSubmissionPreflightValidator.Validate(
            fixture.PackagePath,
            identity,
            sideload,
            Wack("PASS"),
            fixture.FinalScanEvidence(),
            fixture.PackagedHardwareReport(),
            fixture.HardwareArtifactRoot);

        Assert.Contains(result.Issues, issue =>
            issue.Code == "store-package-sha256-mismatch");
    }

    [Fact]
    public void UnreferencedPackagedHardwareArtifactIsRejected()
    {
        using var fixture = new Fixture();
        File.WriteAllText(
            Path.Combine(fixture.HardwareArtifactRoot, "unreviewed.txt"),
            "not part of the report");

        var result = WindowsStoreSubmissionPreflightValidator.Validate(
            fixture.PackagePath,
            fixture.PackagingIdentity(),
            fixture.SideloadEvidence(),
            Wack("PASS"),
            fixture.FinalScanEvidence(),
            fixture.PackagedHardwareReport(),
            fixture.HardwareArtifactRoot);

        Assert.Contains(result.Issues, issue =>
            issue.Code ==
            "store-packaged-hardware-artifact-unreferenced" &&
            issue.Subject == "unreviewed.txt");
    }

    [Fact]
    public void ExactPartnerCenterReportsArePublishEligible()
    {
        using var fixture = new Fixture();
        var certificationReport = Encoding.UTF8.GetBytes(
            "Partner Center certification passed");
        var flightReport = Encoding.UTF8.GetBytes(
            "Partner Center private flight passed");

        var result = WindowsStorePublicReleaseValidator.Validate(
            fixture.PackagePath,
            fixture.PackagingIdentity(),
            fixture.SideloadEvidence(),
            Wack("PASS"),
            fixture.FinalScanEvidence(),
            fixture.PackagedHardwareReport(),
            fixture.HardwareArtifactRoot,
            fixture.PartnerCenterEvidence(
                certificationReport,
                flightReport),
            certificationReport,
            flightReport);

        Assert.True(result.IsPublishEligible);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ChangedPartnerCenterReportIsNotPublishEligible()
    {
        using var fixture = new Fixture();
        var certificationReport = Encoding.UTF8.GetBytes(
            "Partner Center certification passed");
        var flightReport = Encoding.UTF8.GetBytes(
            "Partner Center private flight passed");
        var evidence = fixture.PartnerCenterEvidence(
            certificationReport,
            flightReport);

        var result = WindowsStorePublicReleaseValidator.Validate(
            fixture.PackagePath,
            fixture.PackagingIdentity(),
            fixture.SideloadEvidence(),
            Wack("PASS"),
            fixture.FinalScanEvidence(),
            fixture.PackagedHardwareReport(),
            fixture.HardwareArtifactRoot,
            evidence,
            certificationReport,
            Encoding.UTF8.GetBytes("changed flight report"));

        Assert.False(result.IsPublishEligible);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "partner-center-flight-report-mismatch");
    }

    private static byte[] Wack(string result) => Encoding.UTF8.GetBytes(
        $"<REPORT OVERALL_RESULT=\"{result}\">" +
        $"<TEST NAME=\"all\" RESULT=\"{result}\" />" +
        "</REPORT>");

    private sealed class Fixture : IDisposable
    {
        private const string Publisher = "CN=VR Recorder Test";
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"vrrecorder-store-preflight-{Guid.NewGuid():N}");

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            PackagePath = Path.Combine(_root, "VRRecorder_0.1.0.0_x64.msix");
            File.WriteAllBytes(PackagePath, [1, 2, 3, 4]);
            HardwareArtifactRoot = Path.Combine(_root, "hardware-artifacts");
            Directory.CreateDirectory(HardwareArtifactRoot);
            foreach (var caseId in RequiredPackagedCases)
            {
                File.WriteAllText(
                    Path.Combine(HardwareArtifactRoot, $"{caseId}.txt"),
                    caseId);
            }
        }

        public string PackagePath { get; }

        public string HardwareArtifactRoot { get; }

        public byte[] PackagingIdentity()
        {
            var hash = Hash();
            return Encoding.UTF8.GetBytes("""
                {
                  "schemaVersion": 1,
                  "candidateKind": "microsoft-store-packaging-candidate",
                  "packageFileName": "VRRecorder_0.1.0.0_x64.msix",
                  "packageSha256": "{{HASH}}",
                  "manifestSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "packagingRevision": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                  "validatedArtifact": {},
                  "validatedPayload": {
                    "legalManifestSha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
                  },
                  "hardwareValidationReportSha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                  "storeIdentity": {
                    "packageIdentityName": "VRRecorder.Test",
                    "publisher": "CN=VR Recorder Test",
                    "publisherDisplayName": "VR Recorder Test",
                    "version": "0.1.0.0",
                    "processorArchitecture": "x64"
                  },
                  "publishEligible": false
                }
                """.Replace("{{HASH}}", hash, StringComparison.Ordinal));
        }

        public byte[] SideloadEvidence(
            string certificateSubject = Publisher,
            bool settingsPassed = true)
        {
            return Encoding.UTF8.GetBytes("""
                {
                  "schemaVersion": 1,
                  "evidenceKind": "store-sideload-lifecycle-v1",
                  "packageSha256": "{{HASH}}",
                  "manifestPublisher": "CN=VR Recorder Test",
                  "certificateSubject": "{{SUBJECT}}",
                  "certificateThumbprint": "dddddddddddddddddddddddddddddddddddddddd",
                  "signToolVersion": "10.0.26100.0",
                  "signatureVerified": true,
                  "installSucceeded": true,
                  "launchSucceeded": true,
                  "uninstallSucceeded": true,
                  "installRootReadOnly": true,
                  "workingDirectoryIndependent": true,
                  "settingsPassed": {{SETTINGS}},
                  "diagnosticsPassed": true,
                  "legalDisplayPassed": true,
                  "capturedAtUtc": "2026-07-20T00:00:00Z"
                }
                """
                .Replace("{{HASH}}", Hash(), StringComparison.Ordinal)
                .Replace("{{SUBJECT}}", certificateSubject,
                    StringComparison.Ordinal)
                .Replace("{{SETTINGS}}",
                    settingsPassed ? "true" : "false",
                    StringComparison.Ordinal));
        }

        public byte[] FinalScanEvidence(bool malwareScanPassed = true)
        {
            return Encoding.UTF8.GetBytes("""
                {
                  "schemaVersion": 1,
                  "evidenceKind": "store-final-scan-v1",
                  "packageSha256": "{{HASH}}",
                  "legalManifestSha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
                  "sbomSha256": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
                  "scanner": "Microsoft Defender Antivirus",
                  "scannerVersion": "4.18.26010.5",
                  "definitionVersion": "1.2.3.4",
                  "malwareScanPassed": {{MALWARE}},
                  "legalBundleVerified": true,
                  "sbomPresent": true,
                  "privateKeysAbsent": true,
                  "capturedAtUtc": "2026-07-20T00:00:00Z"
                }
                """
                .Replace("{{HASH}}", Hash(), StringComparison.Ordinal)
                .Replace("{{MALWARE}}",
                    malwareScanPassed ? "true" : "false",
                    StringComparison.Ordinal));
        }

        public byte[] PackagedHardwareReport()
        {
            var cases = RequiredPackagedCases.Select(caseId => new
            {
                caseId,
                status = "passed",
                artifacts = new[]
                {
                    new
                    {
                        relativePath = $"{caseId}.txt",
                        sha256 = FileHash(Path.Combine(
                            HardwareArtifactRoot,
                            $"{caseId}.txt")),
                    },
                },
            });
            var report = new
            {
                schemaVersion = 1,
                matrixProfile = "store-packaged-hardware-validation-v1",
                packageSha256 = Hash(),
                runs = new[]
                {
                    new
                    {
                        runId = "01234567-89ab-cdef-0123-456789abcdef",
                        capturedAtUtc = "2026-07-20T00:00:00Z",
                        environment = new
                        {
                            os = "Windows 11 24H2",
                            gpu = "test GPU",
                            driver = "1.2.3",
                            steamVr = "2.0",
                            hmd = "test HMD",
                        },
                        cases,
                    },
                },
            };
            return JsonSerializer.SerializeToUtf8Bytes(report);
        }

        public byte[] PartnerCenterEvidence(
            byte[] certificationReport,
            byte[] flightReport)
        {
            var evidence = new
            {
                schemaVersion = 1,
                evidenceKind = "partner-center-public-release-v1",
                packageSha256 = Hash(),
                submissionId = "submission-20260720",
                certificationStatus = "passed",
                certificationReportSha256 = Hash(certificationReport),
                flightStatus = "passed",
                flightReportSha256 = Hash(flightReport),
                approvedBy = "release-reviewer",
                capturedAtUtc = "2026-07-20T00:00:00Z",
            };
            return JsonSerializer.SerializeToUtf8Bytes(evidence);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private string Hash()
        {
            using var stream = File.OpenRead(PackagePath);
            return Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
        }

        private static string FileHash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
        }

        private static string Hash(byte[] content) => Convert
            .ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();

        private static readonly string[] RequiredPackagedCases =
        [
            "spout2-wasapi-recording",
            "nvenc-recording",
            "amf-recording",
            "qsv-recording",
            "software-fallback-recording",
            "vrchat-recording",
            "openvr-overlay-controller",
            "wrist-haptics-move-pin-telemetry",
        ];
    }
}
