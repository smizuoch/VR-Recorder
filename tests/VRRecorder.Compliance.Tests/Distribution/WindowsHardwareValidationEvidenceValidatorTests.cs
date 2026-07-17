using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsHardwareValidationEvidenceValidatorTests
{
    [Fact]
    public void RepositoryMatrixProfileMatchesValidatorCaseClosure()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "test-list",
            "windows-hardware-validation-matrix-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        Assert.Equal(
            "full-production-hardware-validation-v1",
            document.RootElement.GetProperty("profile").GetString());
        Assert.Equal(
            WindowsHardwareValidationMatrixProfile.RequiredCaseIds,
            document.RootElement.GetProperty("requiredCaseIds")
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task CompletePassingMatrixAndExactArtifactsIssueEvidence()
    {
        using var fixture = Fixture.Create();

        var result = await WindowsHardwareValidationEvidenceValidator
            .ValidateAsync(
                fixture.IdentityBytes,
                fixture.ReportBytes(),
                fixture.ArtifactRoot,
                CancellationToken.None);

        Assert.True(result.IsValidated);
        Assert.Empty(result.Issues);
        var evidence = Assert.IsType<HardwareValidationEvidence>(
            result.Evidence);
        Assert.Equal("0.1.0", evidence.Payload.ProductVersion);
        Assert.Equal(fixture.Identity.DocumentSha256,
            evidence.PayloadIdentityDocumentSha256);
        Assert.Matches("^[0-9a-f]{64}$", evidence.ValidationReportSha256);
        Assert.Equal(4, evidence.RunIds.Count);
        Assert.Null(typeof(HardwareValidationEvidence).GetProperty("Passed"));
    }

    [Fact]
    public async Task PayloadMismatchAndMissingRequiredCaseIssueNoEvidence()
    {
        using var fixture = Fixture.Create();
        AssertIssue(
            "hardware-validation-payload-identity-mismatch",
            await fixture.ValidateAsync(identityShaOverride: new string('f', 64)));
        AssertIssue(
            "hardware-validation-required-case-missing",
            await fixture.ValidateAsync(
                omittedCase: WindowsHardwareValidationMatrixProfile
                    .RequiredCaseIds[0]));
    }

    [Theory]
    [InlineData("fail", "hardware-validation-case-failed")]
    [InlineData("skip", "hardware-validation-case-skipped")]
    public async Task FailedOrSkippedCaseCannotIssueEvidence(
        string status,
        string expectedCode)
    {
        using var fixture = Fixture.Create();

        AssertIssue(
            expectedCode,
            await fixture.ValidateAsync(
                statusCase: WindowsHardwareValidationMatrixProfile
                    .RequiredCaseIds[0],
                status: status));
    }

    [Fact]
    public async Task MissingTamperedOrUnexpectedArtifactCannotIssueEvidence()
    {
        using var missing = Fixture.Create();
        File.Delete(missing.ArtifactPaths[0]);
        AssertIssue(
            "hardware-validation-artifact-missing",
            await missing.ValidateAsync());

        using var tampered = Fixture.Create();
        await File.AppendAllTextAsync(tampered.ArtifactPaths[0], "tampered");
        AssertIssue(
            "hardware-validation-artifact-mismatch",
            await tampered.ValidateAsync());

        using var unexpected = Fixture.Create();
        await File.WriteAllTextAsync(
            Path.Combine(unexpected.ArtifactRoot, "ambient.json"),
            "ambient");
        AssertIssue(
            "hardware-validation-artifact-unexpected",
            await unexpected.ValidateAsync());
    }

    [Fact]
    public async Task InvalidArtifactRootFailsClosedWithoutEscapingParserErrors()
    {
        using var fixture = Fixture.Create();

        AssertIssue(
            "hardware-validation-artifact-root-invalid",
            await WindowsHardwareValidationEvidenceValidator.ValidateAsync(
                fixture.IdentityBytes,
                fixture.ReportBytes(),
                fixture.ArtifactRoot + '\0',
                CancellationToken.None));
    }

    [Theory]
    [InlineData("windows-10-22h2",
        "hardware-validation-os-profile-missing")]
    [InlineData("amd",
        "hardware-validation-hardware-encoder-matrix-missing")]
    [InlineData("software",
        "hardware-validation-software-fallback-missing")]
    [InlineData("steamvr",
        "hardware-validation-steamvr-controller-missing")]
    public async Task MissingMatrixDimensionCannotIssueEvidence(
        string omittedDimension,
        string expectedCode)
    {
        using var fixture = Fixture.Create();

        AssertIssue(
            expectedCode,
            await fixture.ValidateAsync(omittedDimension: omittedDimension));
    }

    private static void AssertIssue(
        string code,
        WindowsHardwareValidationEvidenceValidation result)
    {
        Assert.False(result.IsValidated);
        Assert.Null(result.Evidence);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VR-Recorder.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class Fixture : IDisposable
    {
        private const string ShaA =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string ShaC =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        private const string SourceRevision =
            "0123456789abcdef0123456789abcdef01234567";
        private readonly IReadOnlyList<CaseSpec> _cases;

        private Fixture(string root)
        {
            Root = root;
            ArtifactRoot = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(ArtifactRoot);
            IdentityBytes = CreateIdentityBytes();
            Identity = WindowsApplicationPayloadIdentityReader.Read(
                IdentityBytes);
            var caseIds = WindowsHardwareValidationMatrixProfile
                .RequiredCaseIds
                .Concat([
                    "matrix-amd-encoder",
                    "matrix-intel-encoder",
                    "matrix-software-fallback",
                ])
                .ToArray();
            var cases = new List<CaseSpec>();
            var artifactPaths = new List<string>();
            foreach (var id in caseIds)
            {
                var relativePath = $"runs/{id}.json";
                var fullPath = Path.Combine(
                    ArtifactRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                var bytes = Encoding.UTF8.GetBytes($"{{\"case\":\"{id}\"}}\n");
                File.WriteAllBytes(fullPath, bytes);
                cases.Add(new CaseSpec(
                    id,
                    relativePath,
                    bytes.LongLength,
                    Sha256(bytes)));
                artifactPaths.Add(fullPath);
            }

            _cases = cases;
            ArtifactPaths = artifactPaths;
        }

        public string Root { get; }

        public string ArtifactRoot { get; }

        public byte[] IdentityBytes { get; }

        public WindowsApplicationPayloadIdentityDocument Identity { get; }

        public List<string> ArtifactPaths { get; }

        public static Fixture Create()
        {
            var root = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "vrrecorder-hardware-evidence-validator-tests",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            return new Fixture(root);
        }

        public Task<WindowsHardwareValidationEvidenceValidation> ValidateAsync(
            string? identityShaOverride = null,
            string? omittedCase = null,
            string? statusCase = null,
            string status = "pass",
            string? omittedDimension = null) =>
            WindowsHardwareValidationEvidenceValidator.ValidateAsync(
                IdentityBytes,
                ReportBytes(
                    identityShaOverride,
                    omittedCase,
                    statusCase,
                    status,
                    omittedDimension),
                ArtifactRoot,
                CancellationToken.None);

        public byte[] ReportBytes(
            string? identityShaOverride = null,
            string? omittedCase = null,
            string? statusCase = null,
            string status = "pass",
            string? omittedDimension = null)
        {
            var runs = Runs(omittedDimension);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteString(
                    "matrixProfile",
                    "full-production-hardware-validation-v1");
                writer.WriteString(
                    "payloadIdentitySha256",
                    identityShaOverride ?? Identity.DocumentSha256);
                writer.WriteStartArray("runs");
                foreach (var run in runs)
                {
                    WriteRun(
                        writer,
                        run,
                        omittedCase,
                        statusCase,
                        status);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return output.ToArray();
        }

        private static IReadOnlyList<RunSpec> Runs(string? omittedDimension)
        {
            var connected = omittedDimension != "steamvr";
            var runs = new List<RunSpec>
            {
                new(
                    "01234567-89ab-4cde-8fab-0123456789ab",
                    "windows-11",
                    "nvidia",
                    "hardware",
                    "nvenc",
                    connected,
                    WindowsHardwareValidationMatrixProfile.RequiredCaseIds),
                new(
                    "11234567-89ab-4cde-8fab-0123456789ab",
                    "windows-10-22h2",
                    "amd",
                    "hardware",
                    "amf",
                    false,
                    ["matrix-amd-encoder"]),
                new(
                    "21234567-89ab-4cde-8fab-0123456789ab",
                    "windows-11",
                    "intel",
                    "hardware",
                    "qsv",
                    false,
                    ["matrix-intel-encoder"]),
                new(
                    "31234567-89ab-4cde-8fab-0123456789ab",
                    "windows-10-22h2",
                    "nvidia",
                    "software",
                    "media-foundation",
                    false,
                    ["matrix-software-fallback"]),
            };
            return omittedDimension switch
            {
                "windows-10-22h2" => runs
                    .Where(run => run.OperatingSystem != "windows-10-22h2")
                    .ToArray(),
                "amd" => runs.Where(run => run.GpuVendor != "amd").ToArray(),
                "software" => runs
                    .Where(run => run.EncoderMode != "software")
                    .ToArray(),
                _ => runs,
            };
        }

        private void WriteRun(
            Utf8JsonWriter writer,
            RunSpec run,
            string? omittedCase,
            string? statusCase,
            string status)
        {
            writer.WriteStartObject();
            writer.WriteString("runId", run.RunId);
            writer.WriteString("runnerId", "local-hil-runner");
            writer.WriteString("capturedAtUtc", "2026-07-17T00:00:00Z");
            writer.WriteStartObject("environment");
            writer.WriteStartObject("os");
            writer.WriteString("profile", run.OperatingSystem);
            writer.WriteString("build", "10.0.26100.4652");
            writer.WriteString("architecture", "x64");
            writer.WriteEndObject();
            writer.WriteStartObject("gpu");
            writer.WriteString("vendor", run.GpuVendor);
            writer.WriteString("deviceId", "1234:5678");
            writer.WriteString("driverVersion", "32.0.1.2");
            writer.WriteEndObject();
            writer.WriteStartObject("encoder");
            writer.WriteString("mode", run.EncoderMode);
            writer.WriteString("api", run.EncoderApi);
            writer.WriteString("name", run.EncoderApi + " H.264");
            writer.WriteEndObject();
            writer.WriteStartObject("steamVr");
            writer.WriteString(
                "runtimeVersion",
                run.SteamVrConnected ? "2.10.0" : "not-connected");
            writer.WriteString(
                "hmdModel",
                run.SteamVrConnected ? "PICO 4" : "not-connected");
            writer.WriteString(
                "leftController",
                run.SteamVrConnected ? "PICO Controller" : "not-connected");
            writer.WriteString(
                "rightController",
                run.SteamVrConnected ? "PICO Controller" : "not-connected");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartArray("cases");
            foreach (var id in run.CaseIds.Where(id => id != omittedCase))
            {
                var spec = _cases.Single(item => item.Id == id);
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteString("status", id == statusCase ? status : "pass");
                writer.WriteStartArray("artifacts");
                writer.WriteStartObject();
                writer.WriteString("path", spec.Path);
                writer.WriteNumber("length", spec.Length);
                writer.WriteString("sha256", spec.Sha256);
                writer.WriteString("kind", "diagnostic");
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static byte[] CreateIdentityBytes()
        {
            var files = new StagedPayloadFile[]
            {
                new(
                    "VRRecorder.App.exe",
                    ShaA,
                    34,
                    StagedArtifactKind.Executable),
            };
            var inventory = new WindowsPublishDirectoryInventory(
                Path.GetFullPath("publish"),
                "VRRecorder.App.exe",
                ShaA,
                WindowsPublishInventoryDigest.Compute(files),
                files);
            return WindowsApplicationPayloadIdentityPublisher.Generate(
                new SealedWindowsApplicationPayload(
                    inventory,
                    new ManagedApplicationBuildIdentity(
                        "0.1.0",
                        SourceRevision,
                        "win-x64"),
                    "win-x64",
                    "legal-id",
                    ShaC));
        }

        private static string Sha256(byte[] bytes) => Convert
            .ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();

        private sealed record CaseSpec(
            string Id,
            string Path,
            long Length,
            string Sha256);

        private sealed record RunSpec(
            string RunId,
            string OperatingSystem,
            string GpuVendor,
            string EncoderMode,
            string EncoderApi,
            bool SteamVrConnected,
            IReadOnlyList<string> CaseIds);
    }
}
