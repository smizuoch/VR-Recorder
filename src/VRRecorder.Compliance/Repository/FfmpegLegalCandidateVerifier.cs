using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

public static class FfmpegLegalCandidateVerifier
{
    private const string ComponentId = "ffmpeg";
    private const string SourceSha256 =
        "464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c";
    private const string SourceCommit =
        "38b88335f99e76ed89ff3c93f877fdefce736c13";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly Dictionary<string, (long Length, string Hash)>
        ExpectedArtifacts = new Dictionary<string, (long, string)>(
            StringComparer.Ordinal)
        {
            ["avcodec-62.dll"] = (
                636928,
                "e399de2feeb8ec0d92eecb3b124205ddc3b5e27c880537cf19a8d2c85d2c6ef2"),
            ["avformat-62.dll"] = (
                620544,
                "73b2ae5addd81234f89e266f6dd827e9122862e6de3d4380cb57e948f3936efc"),
            ["avutil-60.dll"] = (
                984576,
                "c4d6e753ded06793ec484f54798608eb98f8aa4819bd029e68a328b41f25ede7"),
            ["swresample-6.dll"] = (
                201728,
                "0e39d92a9aec1b1f07bac1bd0586c23c8b761777bf4676fff5795d22df5a6de3"),
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
                "invalid-ffmpeg-legal-candidate-registry",
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
                "missing-or-ambiguous-ffmpeg-legal-candidate",
                ComponentId));
            return issues;
        }

        var component = matches[0];
        if (!HasExpectedComponentIdentity(component))
        {
            issues.Add(new ComplianceIssue(
                "invalid-ffmpeg-legal-candidate-identity",
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
                "premature-ffmpeg-legal-admission",
                ComponentId));
        }

        var candidate = component.FfmpegLegalCandidate;
        if (candidate is null || !HasExpectedSourceIdentity(candidate))
        {
            issues.Add(new ComplianceIssue(
                "invalid-ffmpeg-source-candidate",
                ComponentId));
            return issues;
        }

        VerifyRepositoryFile(
            root,
            candidate.SourcePatchPath,
            candidate.SourcePatchLength,
            candidate.SourcePatchSha256,
            "ffmpeg:source-patch",
            issues);
        VerifyRepositoryFile(
            root,
            candidate.BuildRecipePath,
            candidate.BuildRecipeLength,
            candidate.BuildRecipeSha256,
            "ffmpeg:build-recipe",
            issues);
        VerifyRepositoryFile(
            root,
            candidate.SourceOfferPath,
            candidate.SourceOfferLength,
            candidate.SourceOfferSha256,
            "ffmpeg:source-offer",
            issues);
        VerifyArtifacts(candidate.Artifacts, issues);
        VerifySourceOffer(root, candidate, issues);
        return issues;
    }

    private static bool HasExpectedComponentIdentity(RegistryComponent component) =>
        string.Equals(component.DisplayName, "FFmpeg libraries", StringComparison.Ordinal) &&
        string.Equals(component.Version, "8.1.2", StringComparison.Ordinal) &&
        string.Equals(component.Purl, "pkg:generic/ffmpeg@8.1.2", StringComparison.Ordinal) &&
        string.Equals(component.Scope, "runtime-bundled", StringComparison.Ordinal) &&
        string.Equals(component.LicenseDeclared, "LGPL-2.1-or-later", StringComparison.Ordinal) &&
        string.Equals(component.LicenseConcluded, "LGPL-2.1-or-later", StringComparison.Ordinal) &&
        string.Equals(component.Repository.Url, "https://git.ffmpeg.org/ffmpeg.git", StringComparison.Ordinal) &&
        string.Equals(component.Repository.Commit, SourceCommit, StringComparison.Ordinal) &&
        component.Modified && component.Packages.Length == 0;

    private static bool HasExpectedSourceIdentity(
        RegistryFfmpegLegalCandidate candidate) =>
        candidate.SchemaVersion == 1 &&
        string.Equals(candidate.SourceArchiveFileName, "ffmpeg-8.1.2.tar.xz", StringComparison.Ordinal) &&
        candidate.SourceArchiveLength == 11710924 &&
        string.Equals(candidate.SourceArchiveSha256, SourceSha256, StringComparison.Ordinal) &&
        string.Equals(candidate.SourceDownloadUrl, "https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz", StringComparison.Ordinal) &&
        string.Equals(candidate.SourcePatchUpstreamCommit, "cec19d7ddf725896dfbf79a4c308550d83eab5ec", StringComparison.Ordinal) &&
        string.Equals(candidate.SourcePatchUpstreamUrl, "https://code.ffmpeg.org/FFmpeg/FFmpeg/pulls/23039", StringComparison.Ordinal);

    private static void VerifyArtifacts(
        RegistryCandidateArtifact[]? artifacts,
        List<ComplianceIssue> issues)
    {
        if (artifacts is null || artifacts.Length != ExpectedArtifacts.Count)
        {
            issues.Add(new ComplianceIssue(
                "invalid-ffmpeg-artifact-candidate-set",
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
                    "duplicate-ffmpeg-artifact-candidate",
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
                    "invalid-ffmpeg-artifact-candidate",
                    expected.Key));
            }
        }
    }

    private static void VerifySourceOffer(
        string root,
        RegistryFfmpegLegalCandidate candidate,
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
            candidate.SourcePatchSha256,
            candidate.BuildRecipeSha256,
        };
        required.AddRange(ExpectedArtifacts.SelectMany(artifact =>
            new[] { artifact.Key, artifact.Value.Hash }));
        if (content.Contains('<', StringComparison.Ordinal) ||
            content.Contains('>', StringComparison.Ordinal) ||
            required.Any(value => !content.Contains(value, StringComparison.Ordinal)))
        {
            issues.Add(new ComplianceIssue(
                "invalid-ffmpeg-source-offer-candidate",
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
                "invalid-ffmpeg-candidate-file",
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
                "ffmpeg-candidate-file-mismatch",
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
