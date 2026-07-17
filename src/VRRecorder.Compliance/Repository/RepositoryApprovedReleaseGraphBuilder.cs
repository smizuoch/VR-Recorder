using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Repository;

public static class RepositoryApprovedReleaseGraphBuilder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions RegistryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ReleaseEligibilityResult Build(string repositoryRoot)
        => Build(
            repositoryRoot,
            RepositoryComplianceVerifier.VerifyCandidateInputs);

    internal static ReleaseEligibilityResult Build(
        string repositoryRoot,
        Func<string, IReadOnlyList<ComplianceIssue>> candidateVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(candidateVerifier);
        if (!RepositoryEvidenceRoot.TryResolve(
                repositoryRoot,
                out var root))
        {
            return Reject(
                "invalid-repository-evidence-root",
                repositoryRoot);
        }

        ComplianceIssue[] candidateIssues;
        try
        {
            candidateIssues = candidateVerifier(root)
                .OrderBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or InvalidOperationException or
            ArgumentException or KeyNotFoundException)
        {
            return Reject(
                "repository-candidate-evidence-read-failed",
                root);
        }

        if (candidateIssues.Length != 0)
        {
            return new ReleaseEligibilityResult(null, candidateIssues);
        }

        try
        {
            var registryPath = Path.Combine(
                root,
                "third-party",
                "registry.yml");
            var registry = JsonSerializer.Deserialize<RegistryDocument>(
                               File.ReadAllBytes(registryPath),
                               RegistryJsonOptions) ??
                           throw new InvalidDataException(
                               "The component registry is empty.");
            if (registry.SchemaVersion != 1 ||
                registry.RegistryVersion < 1 ||
                registry.Components is null)
            {
                throw new InvalidDataException(
                    "The component registry version is unsupported.");
            }

            var components = registry.Components
                .Where(component => IsRuntimeScope(component.Scope))
                .Select(component => Normalize(root, component))
                .OrderBy(component => component.Id, StringComparer.Ordinal)
                .ToArray();
            if (components.Length == 0)
            {
                return Reject(
                    "missing-runtime-release-components",
                    "third-party/registry.yml");
            }

            return ReleaseEligibilityGate.Evaluate(
                new NormalizedComponentGraph([], components));
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or DecoderFallbackException or
            ArgumentException or KeyNotFoundException)
        {
            return Reject(
                "invalid-repository-release-graph",
                "third-party/registry.yml");
        }
    }

    private static NormalizedComponent Normalize(
        string root,
        RegistryComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.Id) ||
            string.IsNullOrWhiteSpace(component.DisplayName) ||
            string.IsNullOrWhiteSpace(component.Version) ||
            string.IsNullOrWhiteSpace(component.LicenseDeclared) ||
            string.IsNullOrWhiteSpace(component.LicenseConcluded) ||
            string.IsNullOrWhiteSpace(component.CopyrightNotice) ||
            component.Repository is null ||
            string.IsNullOrWhiteSpace(component.Repository.Url) ||
            component.Approval is null ||
            component.Packages is null)
        {
            throw new InvalidDataException(
                "A runtime component is incomplete.");
        }

        var licensePath = ResolveRepositoryFile(
            root,
            component.LicenseFilePath);
        var licenseBytes = File.ReadAllBytes(licensePath);
        var licenseText = StrictUtf8.GetString(licenseBytes);
        var source = string.IsNullOrWhiteSpace(component.Repository.Commit)
            ? component.Repository.Url
            : $"{component.Repository.Url}@{component.Repository.Commit}";
        return new NormalizedComponent(
            component.Id,
            component.DisplayName,
            component.Version,
            new LicenseDecision(
                component.LicenseDeclared,
                component.LicenseConcluded),
            component.CopyrightNotice,
            component.Scope,
            component.Scope,
            component.Modified,
            source,
            licenseText,
            [new VerifiedLegalFile(
                LegalFileKind.License,
                $"LICENSES/{component.Id}/LICENSE.txt",
                component.LicenseFileSha256,
                licenseText)],
            ParseScope(component.Scope),
            new LegalApproval(
                ParseApproval(component.Approval.Status),
                component.Approval.Id,
                component.Approval.RequestedBy ?? string.Empty,
                component.Approval.Reviewer),
            component.Packages
                .Select(package => new NoticePackage(
                    package.Id,
                    package.Version))
                .OrderBy(package => package.Identity, StringComparer.Ordinal)
                .ToArray());
    }

    private static string ResolveRepositoryFile(
        string root,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                "A repository evidence path is invalid.");
        }

        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) +
                         Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(rootPrefix, comparison) || !File.Exists(path))
        {
            throw new InvalidDataException(
                "A repository evidence file is missing.");
        }

        return path;
    }

    private static bool IsRuntimeScope(string scope) => scope is
        "runtime-bundled" or "runtime-linked" or "runtime-asset";

    private static NoticeScope ParseScope(string scope) => scope switch
    {
        "runtime-bundled" => NoticeScope.RuntimeBundled,
        "runtime-linked" => NoticeScope.RuntimeLinked,
        "runtime-asset" => NoticeScope.RuntimeAsset,
        _ => throw new InvalidDataException(
            "A runtime component scope is invalid."),
    };

    private static LegalApprovalStatus ParseApproval(string status) =>
        status switch
        {
            "pending-independent-review" => LegalApprovalStatus.Pending,
            "approved" => LegalApprovalStatus.Approved,
            "rejected" => LegalApprovalStatus.Rejected,
            _ => throw new InvalidDataException(
                "A component approval status is invalid."),
        };

    private static ReleaseEligibilityResult Reject(
        string code,
        string subject) => new(
        null,
        [new ComplianceIssue(code, subject)]);
}
