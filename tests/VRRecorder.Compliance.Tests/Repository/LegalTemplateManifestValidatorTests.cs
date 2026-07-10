using System.Security.Cryptography;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class LegalTemplateManifestValidatorTests
{
    private const string Header =
        "# DESIGN-TIME SNAPSHOT — production release regenerates this manifest from the final Legal Bundle.";

    [Fact]
    public void CanonicalManifestCoveringEveryTemplateFileIsAccepted()
    {
        using var repository = new TemporaryRepository();
        repository.Write("legal-template/README.md", "readme\n");
        repository.Write("legal-template/nested/NOTICE.txt", "notice\n");
        repository.WriteManifest("README.md", "nested/NOTICE.txt");

        var issues = LegalTemplateManifestValidator.Verify(repository.Root);

        Assert.Empty(issues);
    }

    [Fact]
    public void ContentHashMismatchFailsClosed()
    {
        using var repository = new TemporaryRepository();
        repository.Write("legal-template/README.md", "before\n");
        repository.WriteManifest("README.md");
        repository.Write("legal-template/README.md", "after\n");

        var issues = LegalTemplateManifestValidator.Verify(repository.Root);

        var issue = Assert.Single(issues);
        Assert.Equal("legal-template-hash-mismatch", issue.Code);
        Assert.Equal("legal-template/README.md", issue.Subject);
    }

    [Theory]
    [InlineData(
        "xyz  README.md",
        "invalid-legal-template-manifest-entry")]
    [InlineData(
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ../README.md",
        "invalid-legal-template-manifest-entry")]
    [InlineData(
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  /README.md",
        "invalid-legal-template-manifest-entry")]
    [InlineData(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA  README.md",
        "invalid-legal-template-manifest-entry")]
    public void MalformedHashOrUnsafePathFailsClosed(
        string entry,
        string expectedCode)
    {
        using var repository = new TemporaryRepository();
        repository.Write("legal-template/README.md", "readme\n");
        repository.WriteRawManifest(entry);

        var issues = LegalTemplateManifestValidator.Verify(repository.Root);

        Assert.Contains(issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void WindowsEquivalentDuplicatePathFailsClosed()
    {
        using var repository = new TemporaryRepository();
        repository.Write("legal-template/README.md", "readme\n");
        var hash = repository.Hash("legal-template/README.md");
        repository.WriteRawManifest(
            $"{hash}  README.md",
            $"{hash}  readme.md");

        var issues = LegalTemplateManifestValidator.Verify(repository.Root);

        Assert.Contains(issues, issue =>
            issue.Code == "duplicate-legal-template-manifest-path");
    }

    [Fact]
    public void MissingManifestTargetAndUnregisteredTemplateFileFailClosed()
    {
        using var repository = new TemporaryRepository();
        repository.Write("legal-template/README.md", "readme\n");
        repository.WriteRawManifest(
            $"{new string('a', 64)}  missing.txt");

        var issues = LegalTemplateManifestValidator.Verify(repository.Root);

        Assert.Contains(issues, issue =>
            issue.Code == "missing-legal-template-file" &&
            issue.Subject == "legal-template/missing.txt");
        Assert.Contains(issues, issue =>
            issue.Code == "unregistered-legal-template-file" &&
            issue.Subject == "legal-template/README.md");
    }

    [Fact]
    public void FileAddedAfterManifestGenerationFailsInventoryClosed()
    {
        using var repository = new TemporaryRepository();
        repository.Write("legal-template/README.md", "readme\n");
        repository.WriteManifest("README.md");
        repository.Write("legal-template/NEW-ASSET.txt", "unregistered\n");

        var issues = LegalTemplateManifestValidator.Verify(repository.Root);

        var issue = Assert.Single(issues);
        Assert.Equal("unregistered-legal-template-file", issue.Code);
        Assert.Equal("legal-template/NEW-ASSET.txt", issue.Subject);
    }

    [Fact]
    public void ManifestEntriesMustUseCanonicalOrdinalOrder()
    {
        using var repository = new TemporaryRepository();
        repository.Write("legal-template/A.txt", "a\n");
        repository.Write("legal-template/B.txt", "b\n");
        repository.WriteRawManifest(
            $"{repository.Hash("legal-template/B.txt")}  B.txt",
            $"{repository.Hash("legal-template/A.txt")}  A.txt");

        var issues = LegalTemplateManifestValidator.Verify(repository.Root);

        Assert.Contains(issues, issue =>
            issue.Code == "noncanonical-legal-template-manifest-order");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory(
            "vr-recorder-legal-template-");

        public string Root => root.FullName;

        public void Dispose() => root.Delete(recursive: true);

        public void Write(string relativePath, string content)
        {
            var path = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string Hash(string relativePath) => Convert
            .ToHexString(SHA256.HashData(File.ReadAllBytes(Resolve(relativePath))))
            .ToLowerInvariant();

        public void WriteManifest(params string[] paths) => WriteRawManifest(
            paths
                .Order(StringComparer.Ordinal)
                .Select(path =>
                    $"{Hash($"legal-template/{path}")}  {path}")
                .ToArray());

        public void WriteRawManifest(params string[] entries) => Write(
            "legal-template/EXAMPLE-LEGAL-MANIFEST.sha256",
            string.Join('\n', [Header, .. entries, string.Empty]));

        private string Resolve(string relativePath) => Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
