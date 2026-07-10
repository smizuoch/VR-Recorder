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
