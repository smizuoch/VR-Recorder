using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class MsvcRuntimeLegalCandidateVerifierTests
{
    private static readonly string[] RuntimeFiles =
    [
        "msvcp140.dll",
        "msvcp140_atomic_wait.dll",
        "vcruntime140.dll",
        "vcruntime140_1.dll",
    ];

    [Fact]
    public void PinsOfficialX64RuntimeCandidateWithoutPrematureReleaseAdmission()
    {
        var root = FindRepositoryRoot();

        Assert.Empty(MsvcRuntimeLegalCandidateVerifier.Verify(root));

        var registry = NativeArtifactRegistryReader.Read(root);
        foreach (var fileName in RuntimeFiles)
        {
            Assert.Null(NativeArtifactRegistryReader.ValidateBuildDependency(
                root,
                registry,
                "msvc-runtime",
                fileName,
                "windows-x64"));

            var releaseIssue = NativeArtifactRegistryReader.ValidateDependency(
                root,
                registry,
                "msvc-runtime",
                fileName,
                "windows-x64");
            Assert.NotNull(releaseIssue);
            Assert.Equal(
                "missing-native-artifact-registration",
                releaseIssue.Code);
        }
    }

    [Fact]
    public void RejectsEvidenceTamperAndPrematureApproval()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"vrrecorder-msvc-runtime-candidate-{Guid.NewGuid():N}");
        try
        {
            foreach (var relativePath in new[]
                     {
                         "third-party/registry.yml",
                         "third-party/licenses/msvc-runtime/LICENSE.txt",
                         "third-party/source-offers/MSVC-RUNTIME-SOURCE-INFO.candidate.txt",
                     })
            {
                var destination = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(repositoryRoot, relativePath), destination);
            }

            Assert.Empty(MsvcRuntimeLegalCandidateVerifier.Verify(root));

            var offerPath = Path.Combine(
                root,
                "third-party/source-offers/MSVC-RUNTIME-SOURCE-INFO.candidate.txt");
            File.AppendAllText(offerPath, "tampered");
            var issues = MsvcRuntimeLegalCandidateVerifier.Verify(root);
            Assert.Contains(issues, issue =>
                issue.Code == "msvc-runtime-candidate-file-mismatch" &&
                issue.Subject == "msvc-runtime:source-offer");

            File.Copy(
                Path.Combine(
                    repositoryRoot,
                    "third-party/source-offers/MSVC-RUNTIME-SOURCE-INFO.candidate.txt"),
                offerPath,
                overwrite: true);
            var registryPath = Path.Combine(root, "third-party/registry.yml");
            var registry = File.ReadAllText(registryPath);
            var componentStart = registry.IndexOf(
                "\"id\": \"msvc-runtime\"",
                StringComparison.Ordinal);
            Assert.True(componentStart >= 0);
            var approvalStart = registry.IndexOf(
                "\"status\": \"pending-independent-review\"",
                componentStart,
                StringComparison.Ordinal);
            Assert.True(approvalStart >= 0);
            File.WriteAllText(
                registryPath,
                string.Concat(
                    registry.AsSpan(0, approvalStart),
                    "\"status\": \"approved\"",
                    registry.AsSpan(
                        approvalStart +
                        "\"status\": \"pending-independent-review\"".Length)));

            issues = MsvcRuntimeLegalCandidateVerifier.Verify(root);
            Assert.Contains(issues, issue =>
                issue.Code == "premature-msvc-runtime-legal-admission" &&
                issue.Subject == "msvc-runtime");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
