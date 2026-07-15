using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

public static class Spout2LegalCandidateVerifier
{
    private const string ComponentId = "spout2";
    private const string SourceCommit =
        "f49e2f469f8cb25f559a6eaa61a3f5b8173fc100";
    private const string SourceSha256 =
        "9d93cadc7fea63d3e8b26384da8f8f23982a06a07adb0363d75630a99ab1f8f1";
    private const string BinarySha256 =
        "695f20e3505fa0da51b2eb959af359f5d9e2c914bb9676e9118d19f6a5424bf4";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly Dictionary<string, (long Length, string Hash)>
        ExpectedArtifacts = new(StringComparer.Ordinal)
        {
            ["SpoutDX_static.lib"] = (
                1081676,
                "1e9aa2d17d05108af2f8eebb405a8d3b81355cef4633c110efab3886b7867afb"),
            ["Spout_static.lib"] = (
                1441554,
                "ce3fdd36584d0e722f73f7eb26b66335c5948c25933304ba206af6ad32d7edbb"),
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
                "invalid-spout2-legal-candidate-registry",
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
                "missing-or-ambiguous-spout2-legal-candidate",
                ComponentId));
            return issues;
        }

        var component = matches[0];
        if (!HasExpectedComponentIdentity(component))
        {
            issues.Add(new ComplianceIssue(
                "invalid-spout2-legal-candidate-identity",
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
                "premature-spout2-legal-admission",
                ComponentId));
        }

        VerifyRepositoryFile(
            root,
            component.LicenseFilePath,
            1326,
            "7b602b5c652a76ced1c6ff5f3f4c15c37a733230eeb5b8d075f1282b446b10be",
            "spout2:license",
            issues);

        var candidate = component.Spout2LegalCandidate;
        if (candidate is null || !HasExpectedCandidateIdentity(candidate))
        {
            issues.Add(new ComplianceIssue(
                "invalid-spout2-source-candidate",
                ComponentId));
            return issues;
        }
        VerifyRepositoryFile(
            root,
            candidate.BuildRecipePath,
            candidate.BuildRecipeLength,
            candidate.BuildRecipeSha256,
            "spout2:build-recipe",
            issues);
        VerifyRepositoryFile(
            root,
            candidate.SourceOfferPath,
            candidate.SourceOfferLength,
            candidate.SourceOfferSha256,
            "spout2:source-offer",
            issues);
        VerifyArtifacts(candidate.Artifacts, issues);
        VerifySourceOffer(root, candidate, issues);
        return issues;
    }

    private static bool HasExpectedComponentIdentity(
        RegistryComponent component) =>
        string.Equals(
            component.DisplayName,
            "Spout2 DirectX receiver",
            StringComparison.Ordinal) &&
        string.Equals(component.Version, "2.007.017", StringComparison.Ordinal) &&
        string.Equals(
            component.Purl,
            "pkg:github/leadedge/Spout2@2.007.017",
            StringComparison.Ordinal) &&
        string.Equals(component.Scope, "runtime-linked", StringComparison.Ordinal) &&
        string.Equals(component.LicenseDeclared, "BSD-2-Clause", StringComparison.Ordinal) &&
        string.Equals(component.LicenseConcluded, "BSD-2-Clause", StringComparison.Ordinal) &&
        string.Equals(
            component.Repository.Url,
            "https://github.com/leadedge/Spout2",
            StringComparison.Ordinal) &&
        string.Equals(
            component.Repository.Commit,
            SourceCommit,
            StringComparison.Ordinal) &&
        !component.Modified && component.Packages.Length == 0;

    private static bool HasExpectedCandidateIdentity(
        RegistrySpout2LegalCandidate candidate) =>
        candidate.SchemaVersion == 1 &&
        string.Equals(
            candidate.SourceArchiveFileName,
            $"Spout2-{SourceCommit}.tar.gz",
            StringComparison.Ordinal) &&
        candidate.SourceArchiveLength == 4920448 &&
        string.Equals(
            candidate.SourceArchiveSha256,
            SourceSha256,
            StringComparison.Ordinal) &&
        string.Equals(
            candidate.SourceDownloadUrl,
            $"https://github.com/leadedge/Spout2/archive/{SourceCommit}.tar.gz",
            StringComparison.Ordinal) &&
        string.Equals(
            candidate.BinaryArchiveFileName,
            "Spout-SDK-binaries_2-007-017_1.zip",
            StringComparison.Ordinal) &&
        candidate.BinaryArchiveLength == 3472666 &&
        string.Equals(
            candidate.BinaryArchiveSha256,
            BinarySha256,
            StringComparison.Ordinal) &&
        string.Equals(
            candidate.BinaryDownloadUrl,
            "https://github.com/leadedge/Spout2/releases/download/2.007.017/Spout-SDK-binaries_2-007-017_1.zip",
            StringComparison.Ordinal) &&
        string.Equals(candidate.Deployment, "static", StringComparison.Ordinal) &&
        string.Equals(candidate.RuntimeLibrary, "MD", StringComparison.Ordinal);

    private static void VerifyArtifacts(
        RegistryCandidateArtifact[]? artifacts,
        List<ComplianceIssue> issues)
    {
        if (artifacts is null || artifacts.Length != ExpectedArtifacts.Count)
        {
            issues.Add(new ComplianceIssue(
                "invalid-spout2-artifact-candidate-set",
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
                    "duplicate-spout2-artifact-candidate",
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
                    "invalid-spout2-artifact-candidate",
                    expected.Key));
            }
        }
    }

    private static void VerifySourceOffer(
        string root,
        RegistrySpout2LegalCandidate candidate,
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
            SourceSha256,
            BinarySha256,
            candidate.BuildRecipeSha256,
        };
        required.AddRange(ExpectedArtifacts.SelectMany(artifact =>
            new[] { artifact.Key, artifact.Value.Hash }));
        if (content.Contains('<', StringComparison.Ordinal) ||
            content.Contains('>', StringComparison.Ordinal) ||
            required.Any(value => !content.Contains(value, StringComparison.Ordinal)))
        {
            issues.Add(new ComplianceIssue(
                "invalid-spout2-source-offer-candidate",
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
                "invalid-spout2-candidate-file",
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
                "spout2-candidate-file-mismatch",
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
