using System.Text.Json;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class RepositoryComplianceTests
{
    [Fact]
    public void LockedNuGetPackagesHavePinnedCandidateLegalMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();

        var issues = RepositoryComplianceVerifier.VerifyCandidateInputs(repositoryRoot);

        Assert.Empty(issues);
    }

    [Fact]
    public void ComponentCatalogV2TemplateIsCycleFreeAndDocumented()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = Path.Combine(
            repositoryRoot,
            "legal-template",
            "schemas",
            "third-party-components-v2.schema.json");
        var examplePath = Path.Combine(
            repositoryRoot,
            "legal-template",
            "THIRD-PARTY-COMPONENTS.v2.example.json");
        var decisionPath = Path.Combine(
            repositoryRoot,
            "docs",
            "adr",
            "0001-legal-bundle-integrity-v2.md");

        Assert.True(File.Exists(schemaPath), schemaPath);
        Assert.True(File.Exists(examplePath), examplePath);
        Assert.True(File.Exists(decisionPath), decisionPath);
        using var schema = JsonDocument.Parse(File.ReadAllBytes(schemaPath));
        using var example = JsonDocument.Parse(File.ReadAllBytes(examplePath));

        Assert.Equal(
            "urn:vr-recorder:third-party-components:2",
            schema.RootElement.GetProperty("$id").GetString());
        Assert.Equal(
            2,
            schema.RootElement
                .GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetInt32());
        Assert.False(schema.RootElement
            .GetProperty("properties")
            .TryGetProperty("manifestSha256", out _));
        Assert.DoesNotContain(
            schema.RootElement.GetProperty("required").EnumerateArray(),
            item => item.GetString() == "manifestSha256");

        var root = example.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.TryGetProperty("manifestSha256", out _));
        Assert.Equal(
            "LEGAL-MANIFEST.sha256",
            root.GetProperty("integrityManifest")
                .GetProperty("path")
                .GetString());
        Assert.Equal(
            "SHA-256",
            root.GetProperty("integrityManifest")
                .GetProperty("algorithm")
                .GetString());
        var decision = File.ReadAllText(decisionPath);
        Assert.Contains(
            "out-of-band",
            decision,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "fixed point",
            decision,
            StringComparison.OrdinalIgnoreCase);
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
