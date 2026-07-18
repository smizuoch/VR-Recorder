using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

public static class MsvcRuntimeLegalCandidateVerifier
{
    private const string ComponentId = "msvc-runtime";
    private const string LicenseHash =
        "bc4249b090d1653b5467ac87ddecba0d8e101a5d125bfa0c7e1014dd98452bdd";
    private const string PackageHash =
        "4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5";
    private const string PackageUrl =
        "https://download.visualstudio.microsoft.com/download/pr/45d3b8dd-bced-4b37-9974-142f748d710c/4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5/Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix";
    private const string RedistributionListUrl =
        "https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution";
    private const string LicenseSourceUrl =
        "https://visualstudio.microsoft.com/wp-content/uploads/2021/09/Visual-C-Runtime-2015-2022-License-1.docx";
    private const string SignerSubject =
        "CN=Microsoft Corporation, OU=OPC, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly Dictionary<string, (long Length, string Hash)>
        ExpectedArtifacts = new(StringComparer.Ordinal)
        {
            ["msvcp140.dll"] = (
                557728,
                "0f885b509a685d2bbfa652fed26b5fb31d88fbdab0a978c641d1c7b8aa460aa9"),
            ["msvcp140_atomic_wait.dll"] = (
                50304,
                "640b2aefced484d0368eea5bdd06addd0658a3a70a49256e560d6923b404a479"),
            ["vcruntime140.dll"] = (
                124544,
                "d5e4d9a3e835fa679450145d6a7d94e36573a509317111904d9b3712c30d9066"),
            ["vcruntime140_1.dll"] = (
                49792,
                "1f2d41c4aa5db0bc33ebf7b66d72943a817d7ce6cbe880502a9403823633093f"),
        };

    public static IReadOnlyList<ComplianceIssue> Verify(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var registryPath = Path.Combine(root, "third-party", "registry.yml");
        try
        {
            using var stream = File.OpenRead(registryPath);
            var registry = JsonSerializer.Deserialize<RegistryDocument>(
                               stream,
                               JsonOptions)
                           ?? throw new JsonException(
                               "The registry document is null.");
            return Verify(root, registry.Components);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [new ComplianceIssue(
                "invalid-msvc-runtime-legal-candidate-registry",
                "third-party/registry.yml")];
        }
    }

    internal static IReadOnlyList<ComplianceIssue> Verify(
        string root,
        IReadOnlyList<RegistryComponent> components)
    {
        var issues = new List<ComplianceIssue>();
        var matches = components.Where(component => string.Equals(
                component.Id,
                ComponentId,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            issues.Add(new ComplianceIssue(
                "missing-or-ambiguous-msvc-runtime-legal-candidate",
                ComponentId));
            return issues;
        }

        var component = matches[0];
        if (!HasExpectedComponentIdentity(component))
        {
            issues.Add(new ComplianceIssue(
                "invalid-msvc-runtime-legal-candidate-identity",
                ComponentId));
        }
        if (!string.Equals(
                component.Approval.Status,
                "pending-independent-review",
                StringComparison.Ordinal) ||
            component.Approval.Id is not null ||
            component.Approval.Reviewer is not null ||
            component.NativeArtifacts is { Length: > 0 })
        {
            issues.Add(new ComplianceIssue(
                "premature-msvc-runtime-legal-admission",
                ComponentId));
        }

        VerifyRepositoryFile(
            root,
            component.LicenseFilePath,
            6614,
            LicenseHash,
            "msvc-runtime:license",
            issues);

        var candidate = component.MsvcRuntimeLegalCandidate;
        if (candidate is null || !HasExpectedCandidateIdentity(candidate))
        {
            issues.Add(new ComplianceIssue(
                "invalid-msvc-runtime-source-candidate",
                ComponentId));
            return issues;
        }

        VerifyRepositoryFile(
            root,
            candidate.SourceOfferPath,
            candidate.SourceOfferLength,
            candidate.SourceOfferSha256,
            "msvc-runtime:source-offer",
            issues);
        VerifyArtifacts(candidate.Artifacts, issues);
        VerifySourceOffer(root, candidate, issues);
        return issues;
    }

    private static bool HasExpectedComponentIdentity(
        RegistryComponent component) =>
        string.Equals(
            component.DisplayName,
            "Microsoft Visual C++ Runtime 2015-2022",
            StringComparison.Ordinal) &&
        string.Equals(component.Version, "14.44.35211.0", StringComparison.Ordinal) &&
        string.Equals(
            component.Purl,
            "pkg:generic/microsoft-vc-runtime@14.44.35211.0",
            StringComparison.Ordinal) &&
        string.Equals(component.Scope, "runtime-bundled", StringComparison.Ordinal) &&
        string.Equals(
            component.LicenseDeclared,
            "LicenseRef-Microsoft-Visual-Cpp-Runtime-2015-2022",
            StringComparison.Ordinal) &&
        string.Equals(
            component.LicenseConcluded,
            "LicenseRef-Microsoft-Visual-Cpp-Runtime-2015-2022",
            StringComparison.Ordinal) &&
        string.Equals(
            component.LicenseFilePath,
            "third-party/licenses/msvc-runtime/LICENSE.txt",
            StringComparison.Ordinal) &&
        string.Equals(component.LicenseFileSha256, LicenseHash, StringComparison.Ordinal) &&
        string.Equals(
            component.Repository.Url,
            RedistributionListUrl,
            StringComparison.Ordinal) &&
        component.Repository.Commit is null &&
        !component.Modified &&
        component.Packages.Length == 0;

    private static bool HasExpectedCandidateIdentity(
        RegistryMsvcRuntimeLegalCandidate candidate) =>
        candidate.SchemaVersion == 1 &&
        string.Equals(
            candidate.InstallerPackageFileName,
            "Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix",
            StringComparison.Ordinal) &&
        candidate.InstallerPackageLength == 3236248 &&
        string.Equals(candidate.InstallerPackageSha256, PackageHash, StringComparison.Ordinal) &&
        string.Equals(candidate.InstallerPackageDownloadUrl, PackageUrl, StringComparison.Ordinal) &&
        string.Equals(candidate.InstallerPackageSignerSubject, SignerSubject, StringComparison.Ordinal) &&
        string.Equals(candidate.RedistributionListUrl, RedistributionListUrl, StringComparison.Ordinal) &&
        string.Equals(candidate.LicenseSourceUrl, LicenseSourceUrl, StringComparison.Ordinal);

    private static void VerifyArtifacts(
        RegistryCandidateArtifact[]? artifacts,
        List<ComplianceIssue> issues)
    {
        if (artifacts is null || artifacts.Length != ExpectedArtifacts.Count)
        {
            issues.Add(new ComplianceIssue(
                "invalid-msvc-runtime-artifact-candidate-set",
                ComponentId));
            return;
        }

        var actual = new Dictionary<string, RegistryCandidateArtifact>(
            StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            if (!actual.TryAdd(artifact.FileName, artifact))
            {
                issues.Add(new ComplianceIssue(
                    "duplicate-msvc-runtime-artifact-candidate",
                    artifact.FileName));
            }
        }

        foreach (var expected in ExpectedArtifacts)
        {
            if (!actual.TryGetValue(expected.Key, out var artifact) ||
                artifact.Length != expected.Value.Length ||
                !string.Equals(
                    artifact.Sha256,
                    expected.Value.Hash,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "invalid-msvc-runtime-artifact-candidate",
                    expected.Key));
            }
        }
    }

    private static void VerifySourceOffer(
        string root,
        RegistryMsvcRuntimeLegalCandidate candidate,
        List<ComplianceIssue> issues)
    {
        if (!TryResolveRepositoryPath(root, candidate.SourceOfferPath, out var path) ||
            !File.Exists(path))
        {
            return;
        }

        var content = File.ReadAllText(path);
        var required = new List<string>
        {
            "pending independent legal review",
            PackageHash,
            PackageUrl,
            RedistributionListUrl,
            LicenseSourceUrl,
            LicenseHash,
            SignerSubject,
        };
        required.AddRange(ExpectedArtifacts.SelectMany(artifact =>
            new[] { artifact.Key, artifact.Value.Hash }));
        if (content.Contains('<', StringComparison.Ordinal) ||
            content.Contains('>', StringComparison.Ordinal) ||
            required.Any(value => !content.Contains(value, StringComparison.Ordinal)))
        {
            issues.Add(new ComplianceIssue(
                "invalid-msvc-runtime-source-offer-candidate",
                candidate.SourceOfferPath));
        }
    }

    private static void VerifyRepositoryFile(
        string root,
        string relativePath,
        long expectedLength,
        string expectedSha256,
        string subject,
        List<ComplianceIssue> issues)
    {
        if (!IsLowerHexSha256(expectedSha256) ||
            expectedLength <= 0 ||
            !TryResolveRepositoryPath(root, relativePath, out var path) ||
            !File.Exists(path))
        {
            issues.Add(new ComplianceIssue(
                "invalid-msvc-runtime-candidate-file",
                subject));
            return;
        }

        using var stream = File.OpenRead(path);
        var actualHash = Convert
            .ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        if (stream.Length != expectedLength ||
            !string.Equals(actualHash, expectedSha256, StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "msvc-runtime-candidate-file-mismatch",
                subject));
        }
    }

    private static bool TryResolveRepositoryPath(
        string root,
        string relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) +
                         Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(rootPrefix, comparison);
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
