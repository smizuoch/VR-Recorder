using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class OpenVrLegalCandidateVerifierBoundaryTests
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
            (component with { DisplayName = "other" }, "invalid-openvr-legal-candidate-identity"),
            (component with { Version = "0" }, "invalid-openvr-legal-candidate-identity"),
            (component with { Purl = "other" }, "invalid-openvr-legal-candidate-identity"),
            (component with { Scope = "other" }, "invalid-openvr-legal-candidate-identity"),
            (component with { LicenseDeclared = "other" }, "invalid-openvr-legal-candidate-identity"),
            (component with { LicenseConcluded = "other" }, "invalid-openvr-legal-candidate-identity"),
            (component with { LicenseFilePath = "other" }, "invalid-openvr-legal-candidate-identity"),
            (component with { LicenseFileSha256 = new string('0', 64) }, "invalid-openvr-legal-candidate-identity"),
            (component with { Repository = component.Repository with { Url = "other" } }, "invalid-openvr-legal-candidate-identity"),
            (component with { Repository = component.Repository with { Commit = "other" } }, "invalid-openvr-legal-candidate-identity"),
            (component with { Modified = true }, "invalid-openvr-legal-candidate-identity"),
            (component with { Packages = [new RegistryPackage("id", "1", "hash", "hash", NuGetDependencyKind.Direct)] }, "invalid-openvr-legal-candidate-identity"),
            (component with { Approval = approval with { Status = "approved" } }, "premature-openvr-legal-admission"),
            (component with { Approval = approval with { Id = "approval" } }, "premature-openvr-legal-admission"),
            (component with { Approval = approval with { Reviewer = "reviewer" } }, "premature-openvr-legal-admission"),
            (component with { NativeArtifacts = [new RegistryNativeArtifact("win-x64", "a.dll", "hash", "source", "hash", "recipe")] }, "premature-openvr-legal-admission"),
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
        var candidate = component.OpenVrLegalCandidate!;

        Assert.Contains(
            OpenVrLegalCandidateVerifier.Verify(root, []),
            issue => issue.Code == "missing-or-ambiguous-openvr-legal-candidate");
        Assert.Contains(
            OpenVrLegalCandidateVerifier.Verify(root, [component, component]),
            issue => issue.Code == "missing-or-ambiguous-openvr-legal-candidate");
        AssertIssue(
            root,
            component with { OpenVrLegalCandidate = null },
            "invalid-openvr-source-candidate");

        foreach (var mutant in new[]
                 {
                     candidate with { SchemaVersion = 2 },
                     candidate with { SourceArchiveFileName = "other" },
                     candidate with { SourceArchiveLength = 1 },
                     candidate with { SourceArchiveSha256 = new string('0', 64) },
                     candidate with { SourceDownloadUrl = "other" },
                     candidate with { Tag = "other" },
                     candidate with { TagObject = "other" },
                     candidate with { Deployment = "other" },
                 })
        {
            AssertIssue(
                root,
                component with { OpenVrLegalCandidate = mutant },
                "invalid-openvr-source-candidate");
        }
    }

    [Fact]
    public void RejectsArtifactSetAndRepositoryFileDeviations()
    {
        var root = FindRepositoryRoot();
        var component = LoadComponent(root);
        var candidate = component.OpenVrLegalCandidate!;
        var artifacts = candidate.Artifacts;

        AssertIssue(
            root,
            component with
            {
                OpenVrLegalCandidate = candidate with { Artifacts = null! },
            },
            "invalid-openvr-artifact-candidate-set");
        AssertIssue(
            root,
            component with
            {
                OpenVrLegalCandidate = candidate with { Artifacts = artifacts[..^1] },
            },
            "invalid-openvr-artifact-candidate-set");
        AssertIssue(
            root,
            component with
            {
                OpenVrLegalCandidate = candidate with
                {
                    Artifacts = [artifacts[0], artifacts[0], artifacts[2], artifacts[3]],
                },
            },
            "duplicate-openvr-artifact-candidate");
        AssertIssue(
            root,
            component with
            {
                OpenVrLegalCandidate = candidate with
                {
                    Artifacts = ReplaceFirst(
                        artifacts,
                        artifacts[0] with { Length = 1 }),
                },
            },
            "invalid-openvr-artifact-candidate");
        AssertIssue(
            root,
            component with
            {
                OpenVrLegalCandidate = candidate with
                {
                    Artifacts = ReplaceFirst(
                        artifacts,
                        artifacts[0] with { Sha256 = new string('0', 64) }),
                },
            },
            "invalid-openvr-artifact-candidate");

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
                component with { OpenVrLegalCandidate = mutant },
                "invalid-openvr-candidate-file");
        }

        AssertIssue(
            root,
            component with
            {
                OpenVrLegalCandidate = candidate with
                {
                    BuildRecipeLength = candidate.BuildRecipeLength + 1,
                },
            },
            "openvr-candidate-file-mismatch");
        AssertIssue(
            root,
            component with
            {
                OpenVrLegalCandidate = candidate with
                {
                    BuildRecipeSha256 = new string('d', 64),
                },
            },
            "invalid-openvr-source-offer-candidate");
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
            OpenVrLegalCandidateVerifier.Verify(root, [component]),
            issue => issue.Code == code);

    private static RegistryComponent LoadComponent(string root)
    {
        var registry = JsonSerializer.Deserialize<RegistryDocument>(
            File.ReadAllText(Path.Combine(root, "third-party", "registry.yml")),
            RegistryJsonOptions)!;
        return Assert.Single(
            registry.Components,
            component => component.Id == "openvr");
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
