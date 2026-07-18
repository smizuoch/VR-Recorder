using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class NativeArtifactRegistryReaderTests
{
    [Fact]
    public void AdmitsPinnedLibvplCandidateOnlyForDevelopmentBuilds()
    {
        var root = FindRepositoryRoot();
        var registry = NativeArtifactRegistryReader.Read(root);

        Assert.Null(NativeArtifactRegistryReader.ValidateBuildDependency(
            root,
            registry,
            "libvpl",
            "libvpl.dll",
            "windows-x64"));

        var releaseIssue = NativeArtifactRegistryReader.ValidateDependency(
            root,
            registry,
            "libvpl",
            "libvpl.dll",
            "windows-x64");
        Assert.NotNull(releaseIssue);
        Assert.Equal(
            "missing-native-artifact-registration",
            releaseIssue.Code);
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
