using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class FfmpegLegalCandidateVerifierTests
{
    private static readonly JsonSerializerOptions RegistryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    [Fact]
    public void RejectsEveryComponentIdentityAndApprovalDeviation()
    {
        var root = FindRepositoryRoot();
        var component = LoadComponent(root);
        var approval = component.Approval;
        var mutants = new (RegistryComponent Component, string Code)[]
        {
            (component with { DisplayName = "other" }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Version = "0" }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Purl = "other" }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Scope = "other" }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { LicenseDeclared = "other" }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { LicenseConcluded = "other" }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Repository = component.Repository with { Url = "other" } }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Repository = component.Repository with { Commit = "other" } }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Modified = false }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Packages = [new RegistryPackage("id", "1", "hash", "hash", NuGetDependencyKind.Direct)] }, "invalid-ffmpeg-legal-candidate-identity"),
            (component with { Approval = approval with { Status = "approved" } }, "premature-ffmpeg-legal-admission"),
            (component with { Approval = approval with { Id = "approval" } }, "premature-ffmpeg-legal-admission"),
            (component with { Approval = approval with { Reviewer = "reviewer" } }, "premature-ffmpeg-legal-admission"),
            (component with { NativeArtifacts = [new RegistryNativeArtifact("win-x64", "a.dll", "hash", "source", "hash", "recipe")] }, "premature-ffmpeg-legal-admission"),
        };

        foreach (var mutant in mutants)
        {
            AssertIssue(root, mutant.Component, mutant.Code);
        }
    }

    [Fact]
    public void RejectsMissingAmbiguousAndEverySourceIdentityDeviation()
    {
        var root = FindRepositoryRoot();
        var component = LoadComponent(root);
        var candidate = component.FfmpegLegalCandidate!;

        Assert.Contains(
            FfmpegLegalCandidateVerifier.Verify(root, []),
            issue => issue.Code == "missing-or-ambiguous-ffmpeg-legal-candidate");
        Assert.Contains(
            FfmpegLegalCandidateVerifier.Verify(root, [component, component]),
            issue => issue.Code == "missing-or-ambiguous-ffmpeg-legal-candidate");
        AssertIssue(
            root,
            component with { FfmpegLegalCandidate = null },
            "invalid-ffmpeg-source-candidate");

        var mutants = new[]
        {
            candidate with { SchemaVersion = 2 },
            candidate with { SourceArchiveFileName = "other" },
            candidate with { SourceArchiveLength = 1 },
            candidate with { SourceArchiveSha256 = new string('0', 64) },
            candidate with { SourceDownloadUrl = "other" },
            candidate with { SourcePatchUpstreamCommit = "other" },
            candidate with { SourcePatchUpstreamUrl = "other" },
        };
        foreach (var mutant in mutants)
        {
            AssertIssue(
                root,
                component with { FfmpegLegalCandidate = mutant },
                "invalid-ffmpeg-source-candidate");
        }
    }

    [Fact]
    public void RejectsArtifactSetAndRepositoryFileDeviations()
    {
        var root = FindRepositoryRoot();
        var component = LoadComponent(root);
        var candidate = component.FfmpegLegalCandidate!;
        var artifacts = candidate.Artifacts;

        AssertIssue(
            root,
            component with
            {
                FfmpegLegalCandidate = candidate with { Artifacts = null! },
            },
            "invalid-ffmpeg-artifact-candidate-set");
        AssertIssue(
            root,
            component with
            {
                FfmpegLegalCandidate = candidate with { Artifacts = artifacts[..^1] },
            },
            "invalid-ffmpeg-artifact-candidate-set");
        AssertIssue(
            root,
            component with
            {
                FfmpegLegalCandidate = candidate with
                {
                    Artifacts = [artifacts[0], artifacts[0], artifacts[2], artifacts[3]],
                },
            },
            "duplicate-ffmpeg-artifact-candidate");
        AssertIssue(
            root,
            component with
            {
                FfmpegLegalCandidate = candidate with
                {
                    Artifacts = ReplaceFirst(
                        artifacts,
                        artifacts[0] with { Length = 1 }),
                },
            },
            "invalid-ffmpeg-artifact-candidate");
        AssertIssue(
            root,
            component with
            {
                FfmpegLegalCandidate = candidate with
                {
                    Artifacts = ReplaceFirst(
                        artifacts,
                        artifacts[0] with { Sha256 = new string('0', 64) }),
                },
            },
            "invalid-ffmpeg-artifact-candidate");

        foreach (var mutant in new[]
                 {
                     candidate with { SourcePatchSha256 = "invalid" },
                     candidate with { SourcePatchLength = 0 },
                     candidate with { SourcePatchPath = Path.GetFullPath("outside.patch") },
                     candidate with { SourcePatchPath = "missing.patch" },
                 })
        {
            AssertIssue(
                root,
                component with { FfmpegLegalCandidate = mutant },
                "invalid-ffmpeg-candidate-file");
        }

        AssertIssue(
            root,
            component with
            {
                FfmpegLegalCandidate = candidate with
                {
                    SourcePatchLength = candidate.SourcePatchLength + 1,
                },
            },
            "ffmpeg-candidate-file-mismatch");
        AssertIssue(
            root,
            component with
            {
                FfmpegLegalCandidate = candidate with
                {
                    SourcePatchSha256 = new string('d', 64),
                },
            },
            "invalid-ffmpeg-source-offer-candidate");
    }

    private static RegistryCandidateArtifact[] ReplaceFirst(
        RegistryCandidateArtifact[] artifacts,
        RegistryCandidateArtifact replacement) =>
        [replacement, .. artifacts[1..]];

    private static void AssertIssue(
        string root,
        RegistryComponent component,
        string code) =>
        Assert.Contains(
            FfmpegLegalCandidateVerifier.Verify(root, [component]),
            issue => issue.Code == code);

    private static RegistryComponent LoadComponent(string root)
    {
        var registry = JsonSerializer.Deserialize<RegistryDocument>(
            File.ReadAllText(Path.Combine(root, "third-party", "registry.yml")),
            RegistryJsonOptions)!;
        return Assert.Single(
            registry.Components,
            component => component.Id == "ffmpeg");
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
}
