using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsStorePackagedHardwareValidation(
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsValidated => Issues.Count == 0;
}

internal static class WindowsStorePackagedHardwareEvidenceValidator
{
    private const int MaximumReportBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] RequiredCaseIds =
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
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "matrixProfile",
        "packageSha256",
        "runs",
    ];
    private static readonly string[] RunProperties =
    [
        "runId",
        "capturedAtUtc",
        "environment",
        "cases",
    ];
    private static readonly string[] EnvironmentProperties =
    [
        "os",
        "gpu",
        "driver",
        "steamVr",
        "hmd",
    ];
    private static readonly string[] CaseProperties =
    [
        "caseId",
        "status",
        "artifacts",
    ];
    private static readonly string[] ArtifactProperties =
    [
        "relativePath",
        "sha256",
    ];

    public static WindowsStorePackagedHardwareValidation Validate(
        byte[] reportContent,
        string artifactRoot,
        string expectedPackageSha256)
    {
        ArgumentNullException.ThrowIfNull(reportContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPackageSha256);
        if (reportContent.Length == 0 ||
            reportContent.Length > MaximumReportBytes ||
            !Directory.Exists(artifactRoot))
        {
            return Reject(
                "store-packaged-hardware-report-invalid",
                "report-or-artifact-root");
        }

        try
        {
            using var document = JsonDocument.Parse(
                StrictUtf8.GetString(reportContent),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
            var root = document.RootElement;
            RequireExactProperties(root, RootProperties);
            if (RequiredInt32(root, "schemaVersion") != 1 ||
                RequiredString(root, "matrixProfile") !=
                "store-packaged-hardware-validation-v1")
            {
                throw Invalid();
            }

            var packageSha256 = RequiredSha256(root, "packageSha256");
            var issues = new List<ComplianceIssue>();
            if (!string.Equals(
                    packageSha256,
                    expectedPackageSha256,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "store-packaged-hardware-package-mismatch",
                    packageSha256));
            }

            var runsElement = root.GetProperty("runs");
            if (runsElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid();
            }
            var runs = runsElement.EnumerateArray().ToArray();
            if (runs.Length is < 1 or > 64)
            {
                throw Invalid();
            }

            var seenRunIds = new HashSet<string>(StringComparer.Ordinal);
            var seenCaseIds = new HashSet<string>(StringComparer.Ordinal);
            var referencedArtifacts = new HashSet<string>(
                StringComparer.Ordinal);
            var passedCases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var run in runs)
            {
                ParseRun(
                    run,
                    artifactRoot,
                    seenRunIds,
                    seenCaseIds,
                    referencedArtifacts,
                    passedCases,
                    issues);
            }
            foreach (var caseId in RequiredCaseIds.Where(caseId =>
                         !passedCases.Contains(caseId)))
            {
                issues.Add(new ComplianceIssue(
                    "store-packaged-hardware-case-required",
                    caseId));
            }
            AddUnreferencedArtifactIssues(
                artifactRoot,
                referencedArtifacts,
                issues);

            return new WindowsStorePackagedHardwareValidation(Order(issues));
        }
        catch (Exception exception) when (exception is
            JsonException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or
            ArgumentException or IOException or UnauthorizedAccessException or
            FormatException)
        {
            return Reject(
                "store-packaged-hardware-report-invalid",
                "report-or-artifact-root");
        }
    }

    private static void ParseRun(
        JsonElement run,
        string artifactRoot,
        HashSet<string> seenRunIds,
        HashSet<string> seenCaseIds,
        HashSet<string> referencedArtifacts,
        HashSet<string> passedCases,
        List<ComplianceIssue> issues)
    {
        RequireExactProperties(run, RunProperties);
        var runId = RequiredString(run, "runId");
        if (!Guid.TryParseExact(runId, "D", out _) ||
            runId.Any(character => character is >= 'A' and <= 'F') ||
            !seenRunIds.Add(runId))
        {
            throw Invalid();
        }
        var capturedAt = RequiredString(run, "capturedAtUtc");
        if (!DateTimeOffset.TryParseExact(
                capturedAt,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out _))
        {
            throw Invalid();
        }

        var environment = run.GetProperty("environment");
        RequireExactProperties(environment, EnvironmentProperties);
        foreach (var property in EnvironmentProperties)
        {
            _ = RequiredString(environment, property);
        }

        var casesElement = run.GetProperty("cases");
        if (casesElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }
        var cases = casesElement.EnumerateArray().ToArray();
        if (cases.Length is < 1 or > 128)
        {
            throw Invalid();
        }
        foreach (var testCase in cases)
        {
            ParseCase(
                testCase,
                artifactRoot,
                seenCaseIds,
                referencedArtifacts,
                passedCases,
                issues);
        }
    }

    private static void ParseCase(
        JsonElement testCase,
        string artifactRoot,
        HashSet<string> seenCaseIds,
        HashSet<string> referencedArtifacts,
        HashSet<string> passedCases,
        List<ComplianceIssue> issues)
    {
        RequireExactProperties(testCase, CaseProperties);
        var caseId = RequiredString(testCase, "caseId");
        var status = RequiredString(testCase, "status");
        if (!seenCaseIds.Add(caseId) ||
            status is not ("passed" or "failed" or "blocked"))
        {
            throw Invalid();
        }
        if (!RequiredCaseIds.Contains(caseId, StringComparer.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "store-packaged-hardware-case-unexpected",
                caseId));
        }
        var artifactsElement = testCase.GetProperty("artifacts");
        if (artifactsElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }
        var artifacts = artifactsElement.EnumerateArray().ToArray();
        if (artifacts.Length is < 1 or > 32)
        {
            throw Invalid();
        }

        var artifactIssues = new List<ComplianceIssue>();
        foreach (var artifact in artifacts)
        {
            RequireExactProperties(artifact, ArtifactProperties);
            var relativePath = RequiredString(artifact, "relativePath");
            var expectedSha256 = RequiredSha256(artifact, "sha256");
            if (!referencedArtifacts.Add(relativePath))
            {
                throw Invalid();
            }
            if (!TryResolveArtifactPath(
                    artifactRoot,
                    relativePath,
                    out var path) ||
                !File.Exists(path) ||
                ContainsReparsePoint(artifactRoot, path))
            {
                artifactIssues.Add(new ComplianceIssue(
                    "store-packaged-hardware-artifact-invalid",
                    $"{caseId}:{relativePath}"));
                continue;
            }
            using var stream = File.OpenRead(path);
            var actualSha256 = Convert
                .ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
            if (!string.Equals(
                    actualSha256,
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                artifactIssues.Add(new ComplianceIssue(
                    "store-packaged-hardware-artifact-mismatch",
                    $"{caseId}:{relativePath}"));
            }
        }
        issues.AddRange(artifactIssues);
        if (status == "passed" && artifactIssues.Count == 0 &&
            RequiredCaseIds.Contains(caseId, StringComparer.Ordinal))
        {
            _ = passedCases.Add(caseId);
        }
        else if (status != "passed")
        {
            issues.Add(new ComplianceIssue(
                "store-packaged-hardware-case-not-passed",
                caseId));
        }
    }

    private static bool TryResolveArtifactPath(
        string root,
        string relativePath,
        out string path)
    {
        path = string.Empty;
        if (relativePath.Length > 240 ||
            relativePath.Contains('\\') ||
            relativePath.Contains(':') ||
            relativePath.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }
        var canonicalRoot = Path.GetFullPath(root);
        path = Path.GetFullPath(Path.Combine(
            canonicalRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(canonicalRoot) +
                     Path.DirectorySeparatorChar;
        return path.StartsWith(
            prefix,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root));
        if ((File.GetAttributes(canonicalRoot) &
             FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        var relative = Path.GetRelativePath(canonicalRoot, path);
        var current = canonicalRoot;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) &
                 FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static void AddUnreferencedArtifactIssues(
        string artifactRoot,
        HashSet<string> referencedArtifacts,
        List<ComplianceIssue> issues)
    {
        foreach (var file in Directory.EnumerateFiles(
                     artifactRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (ContainsReparsePoint(artifactRoot, file))
            {
                throw Invalid();
            }
            var relativePath = Path.GetRelativePath(artifactRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!referencedArtifacts.Contains(relativePath))
            {
                issues.Add(new ComplianceIssue(
                    "store-packaged-hardware-artifact-unreferenced",
                    relativePath));
            }
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
            actual.Length != expected.Length ||
            expected.Any(name =>
                !actual.Contains(name, StringComparer.Ordinal)))
        {
            throw Invalid();
        }
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        return value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : throw Invalid();
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        return string.IsNullOrWhiteSpace(value) || value.Length > 512
            ? throw Invalid()
            : value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : throw Invalid();
    }

    private static WindowsStorePackagedHardwareValidation Reject(
        string code,
        string subject) => new([new ComplianceIssue(code, subject)]);

    private static ComplianceIssue[] Order(
        IEnumerable<ComplianceIssue> issues) => issues
        .OrderBy(issue => issue.Code, StringComparer.Ordinal)
        .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
        .ToArray();

    private static InvalidDataException Invalid() => new();
}
