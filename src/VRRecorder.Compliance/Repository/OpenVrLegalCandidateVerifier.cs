using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

public static class OpenVrLegalCandidateVerifier
{
    private const string ComponentId = "openvr";
    private const string SourceCommit =
        "0924064316de3effbcd1acf1e309182a2deb1c05";
    private const string TagObject =
        "41bc3825fd35b04047610c86fee26fb33b017b29";
    private const string SourceSha256 =
        "e184cb625010fab7043a9d5e1e000fdeb3067a152bb3169ef53f64dfac37164c";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly Dictionary<string, (long Length, string Hash)>
        ExpectedArtifacts = new(StringComparer.Ordinal)
        {
            ["openvr.h"] = (
                296217,
                "1e6ed57199896cc1f7c5484e50fa18955e97be15be690beb28d998c877ead7fd"),
            ["openvr_api.lib"] = (
                5500,
                "a0bf57c5920f569e8d21ab3e5bc95bac4b73e2016217f8b5b93495a2a7197bbb"),
            ["openvr_api.dll"] = (
                837272,
                "bab8ac6ef64e68a9ca53315b0014d131088584b2efdfa6db511d67ec03cfcb4a"),
            ["openvr_api.dll.sig"] = (
                1450,
                "6a47bb6e5e3d6850aef60abf4fb6b6f1799bee65f2af3bbdc89dac00b843bc5b"),
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
                "invalid-openvr-legal-candidate-registry",
                "third-party/registry.yml")];
        }
    }

    internal static IReadOnlyList<ComplianceIssue> Verify(
        string root,
        IReadOnlyList<RegistryComponent> components)
    {
        var issues = new List<ComplianceIssue>();
        var matches = components
            .Where(component => string.Equals(
                component.Id,
                ComponentId,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            issues.Add(new ComplianceIssue(
                "missing-or-ambiguous-openvr-legal-candidate",
                ComponentId));
            return issues;
        }

        var component = matches[0];
        if (!HasExpectedComponentIdentity(component))
        {
            issues.Add(new ComplianceIssue(
                "invalid-openvr-legal-candidate-identity",
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
                "premature-openvr-legal-admission",
                ComponentId));
        }

        VerifyRepositoryFile(
            root,
            component.LicenseFilePath,
            1488,
            "f56ff606104d4ef18e617921a75c73ad73b5a1a1d70c69590c29de16919e04ad",
            "openvr:license",
            issues);

        var candidate = component.OpenVrLegalCandidate;
        if (candidate is null || !HasExpectedCandidateIdentity(candidate))
        {
            issues.Add(new ComplianceIssue(
                "invalid-openvr-source-candidate",
                ComponentId));
            return issues;
        }
        VerifyRepositoryFile(
            root,
            candidate.BuildRecipePath,
            candidate.BuildRecipeLength,
            candidate.BuildRecipeSha256,
            "openvr:build-recipe",
            issues);
        VerifyRepositoryFile(
            root,
            candidate.SourceOfferPath,
            candidate.SourceOfferLength,
            candidate.SourceOfferSha256,
            "openvr:source-offer",
            issues);
        VerifyArtifacts(candidate.Artifacts, issues);
        VerifySourceOffer(root, candidate, issues);
        return issues;
    }

    private static bool HasExpectedComponentIdentity(
        RegistryComponent component) =>
        string.Equals(
            component.DisplayName,
            "OpenVR SDK runtime",
            StringComparison.Ordinal) &&
        string.Equals(component.Version, "2.15.6", StringComparison.Ordinal) &&
        string.Equals(
            component.Purl,
            "pkg:github/ValveSoftware/openvr@2.15.6",
            StringComparison.Ordinal) &&
        string.Equals(component.Scope, "runtime-bundled", StringComparison.Ordinal) &&
        string.Equals(component.LicenseDeclared, "BSD-3-Clause", StringComparison.Ordinal) &&
        string.Equals(component.LicenseConcluded, "BSD-3-Clause", StringComparison.Ordinal) &&
        string.Equals(
            component.LicenseFilePath,
            "third-party/licenses/openvr/LICENSE.txt",
            StringComparison.Ordinal) &&
        string.Equals(
            component.LicenseFileSha256,
            "f56ff606104d4ef18e617921a75c73ad73b5a1a1d70c69590c29de16919e04ad",
            StringComparison.Ordinal) &&
        string.Equals(
            component.Repository.Url,
            "https://github.com/ValveSoftware/openvr",
            StringComparison.Ordinal) &&
        string.Equals(
            component.Repository.Commit,
            SourceCommit,
            StringComparison.Ordinal) &&
        !component.Modified && component.Packages.Length == 0;

    private static bool HasExpectedCandidateIdentity(
        RegistryOpenVrLegalCandidate candidate) =>
        candidate.SchemaVersion == 1 &&
        string.Equals(
            candidate.SourceArchiveFileName,
            "OpenVR-v2.15.6.tar.gz",
            StringComparison.Ordinal) &&
        candidate.SourceArchiveLength == 154998016 &&
        string.Equals(
            candidate.SourceArchiveSha256,
            SourceSha256,
            StringComparison.Ordinal) &&
        string.Equals(
            candidate.SourceDownloadUrl,
            "https://codeload.github.com/ValveSoftware/openvr/tar.gz/refs/tags/v2.15.6",
            StringComparison.Ordinal) &&
        string.Equals(candidate.Tag, "v2.15.6", StringComparison.Ordinal) &&
        string.Equals(candidate.TagObject, TagObject, StringComparison.Ordinal) &&
        string.Equals(candidate.Deployment, "dynamic", StringComparison.Ordinal);

    private static void VerifyArtifacts(
        RegistryCandidateArtifact[]? artifacts,
        List<ComplianceIssue> issues)
    {
        if (artifacts is null || artifacts.Length != ExpectedArtifacts.Count)
        {
            issues.Add(new ComplianceIssue(
                "invalid-openvr-artifact-candidate-set",
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
                    "duplicate-openvr-artifact-candidate",
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
                    "invalid-openvr-artifact-candidate",
                    expected.Key));
            }
        }
    }

    private static void VerifySourceOffer(
        string root,
        RegistryOpenVrLegalCandidate candidate,
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
            SourceCommit,
            TagObject,
            SourceSha256,
            candidate.BuildRecipeSha256,
        };
        required.AddRange(ExpectedArtifacts.SelectMany(artifact =>
            new[] { artifact.Key, artifact.Value.Hash }));
        if (content.Contains('<', StringComparison.Ordinal) ||
            content.Contains('>', StringComparison.Ordinal) ||
            required.Any(value => !content.Contains(value, StringComparison.Ordinal)))
        {
            issues.Add(new ComplianceIssue(
                "invalid-openvr-source-offer-candidate",
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
                "invalid-openvr-candidate-file",
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
                "openvr-candidate-file-mismatch",
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
