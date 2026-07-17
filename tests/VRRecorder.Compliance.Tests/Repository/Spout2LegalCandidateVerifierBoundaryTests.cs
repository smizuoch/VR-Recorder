using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class Spout2LegalCandidateVerifierBoundaryTests
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
            (component with { DisplayName = "other" }, "invalid-spout2-legal-candidate-identity"),
            (component with { Version = "0" }, "invalid-spout2-legal-candidate-identity"),
            (component with { Purl = "other" }, "invalid-spout2-legal-candidate-identity"),
            (component with { Scope = "other" }, "invalid-spout2-legal-candidate-identity"),
            (component with { LicenseDeclared = "other" }, "invalid-spout2-legal-candidate-identity"),
            (component with { LicenseConcluded = "other" }, "invalid-spout2-legal-candidate-identity"),
            (component with { Repository = component.Repository with { Url = "other" } }, "invalid-spout2-legal-candidate-identity"),
            (component with { Repository = component.Repository with { Commit = "other" } }, "invalid-spout2-legal-candidate-identity"),
            (component with { Modified = true }, "invalid-spout2-legal-candidate-identity"),
            (component with { Packages = [new RegistryPackage("id", "1", "hash", "hash", NuGetDependencyKind.Direct)] }, "invalid-spout2-legal-candidate-identity"),
            (component with { Approval = approval with { Status = "approved" } }, "premature-spout2-legal-admission"),
            (component with { Approval = approval with { Id = "approval" } }, "premature-spout2-legal-admission"),
            (component with { Approval = approval with { Reviewer = "reviewer" } }, "premature-spout2-legal-admission"),
            (component with { NativeArtifacts = [new RegistryNativeArtifact("win-x64", "a.lib", "hash", "source", "hash", "recipe")] }, "premature-spout2-legal-admission"),
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
        var candidate = component.Spout2LegalCandidate!;

        Assert.Contains(
            Spout2LegalCandidateVerifier.Verify(root, []),
            issue => issue.Code == "missing-or-ambiguous-spout2-legal-candidate");
        Assert.Contains(
            Spout2LegalCandidateVerifier.Verify(root, [component, component]),
            issue => issue.Code == "missing-or-ambiguous-spout2-legal-candidate");
        AssertIssue(
            root,
            component with { Spout2LegalCandidate = null },
            "invalid-spout2-source-candidate");

        foreach (var mutant in new[]
                 {
                     candidate with { SchemaVersion = 2 },
                     candidate with { SourceArchiveFileName = "other" },
                     candidate with { SourceArchiveLength = 1 },
                     candidate with { SourceArchiveSha256 = new string('0', 64) },
                     candidate with { SourceDownloadUrl = "other" },
                     candidate with { BinaryArchiveFileName = "other" },
                     candidate with { BinaryArchiveLength = 1 },
                     candidate with { BinaryArchiveSha256 = new string('0', 64) },
                     candidate with { BinaryDownloadUrl = "other" },
                     candidate with { Deployment = "other" },
                     candidate with { RuntimeLibrary = "other" },
                 })
        {
            AssertIssue(
                root,
                component with { Spout2LegalCandidate = mutant },
                "invalid-spout2-source-candidate");
        }
    }

    [Fact]
    public void RejectsArtifactSetAndRepositoryFileDeviations()
    {
        var root = FindRepositoryRoot();
        var component = LoadComponent(root);
        var candidate = component.Spout2LegalCandidate!;
        var artifacts = candidate.Artifacts;

        AssertIssue(
            root,
            component with
            {
                Spout2LegalCandidate = candidate with { Artifacts = null! },
            },
            "invalid-spout2-artifact-candidate-set");
        AssertIssue(
            root,
            component with
            {
                Spout2LegalCandidate = candidate with { Artifacts = artifacts[..^1] },
            },
            "invalid-spout2-artifact-candidate-set");
        AssertIssue(
            root,
            component with
            {
                Spout2LegalCandidate = candidate with
                {
                    Artifacts = [artifacts[0], artifacts[0]],
                },
            },
            "duplicate-spout2-artifact-candidate");
        AssertIssue(
            root,
            component with
            {
                Spout2LegalCandidate = candidate with
                {
                    Artifacts = ReplaceFirst(
                        artifacts,
                        artifacts[0] with { Length = 1 }),
                },
            },
            "invalid-spout2-artifact-candidate");
        AssertIssue(
            root,
            component with
            {
                Spout2LegalCandidate = candidate with
                {
                    Artifacts = ReplaceFirst(
                        artifacts,
                        artifacts[0] with { Sha256 = new string('0', 64) }),
                },
            },
            "invalid-spout2-artifact-candidate");

        foreach (var mutant in new[]
                 {
                     candidate with { BuildRecipeSha256 = "invalid" },
                     candidate with { BuildRecipeLength = 0 },
                     candidate with { BuildRecipePath = Path.GetFullPath("outside.md") },
                     candidate with { BuildRecipePath = "missing.md" },
                 })
        {
            AssertIssue(
                root,
                component with { Spout2LegalCandidate = mutant },
                "invalid-spout2-candidate-file");
        }

        AssertIssue(
            root,
            component with
            {
                Spout2LegalCandidate = candidate with
                {
                    BuildRecipeLength = candidate.BuildRecipeLength + 1,
                },
            },
            "spout2-candidate-file-mismatch");
        AssertIssue(
            root,
            component with
            {
                Spout2LegalCandidate = candidate with
                {
                    BuildRecipeSha256 = new string('d', 64),
                },
            },
            "invalid-spout2-source-offer-candidate");
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
            Spout2LegalCandidateVerifier.Verify(root, [component]),
            issue => issue.Code == code);

    private static RegistryComponent LoadComponent(string root)
    {
        var registry = JsonSerializer.Deserialize<RegistryDocument>(
            File.ReadAllText(Path.Combine(root, "third-party", "registry.yml")),
            RegistryJsonOptions)!;
        return Assert.Single(
            registry.Components,
            component => component.Id == "spout2");
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
