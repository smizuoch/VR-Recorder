namespace VRRecorder.Compliance.Tests.Repository;

public sealed class WindowsHardwareValidationPublishScriptContractTests
{
    [Fact]
    public void ScriptStagesThenPublishesSelfContainedWinX64WithoutDirectRuntimeInputs()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "publish-windows-hardware-validation.ps1"));

        var staging = script.IndexOf(
            "stage-windows-runtime",
            StringComparison.Ordinal);
        var publish = script.IndexOf(
            "dotnet publish",
            StringComparison.Ordinal);
        var seal = script.IndexOf(
            "seal-windows-payload",
            StringComparison.Ordinal);
        Assert.True(staging >= 0);
        Assert.True(publish > staging);
        Assert.True(seal > publish);
        Assert.Contains("--self-contained", script, StringComparison.Ordinal);
        Assert.Contains("win-x64", script, StringComparison.Ordinal);
        Assert.Contains(
            "RestoreLockedMode=true",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApprovedWindowsRuntimeProps",
            script,
            StringComparison.Ordinal);
        Assert.Contains("rev-parse --verify HEAD", script);
        Assert.Contains("SourceRevisionId", script);
        Assert.Contains("--identity-output", script);
        Assert.Contains("application-payload-identity.v1.json", script);
        Assert.DoesNotContain(
            "NativeMediaLibraryPath",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FfprobeExecutablePath",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FfmpegRuntimeDirectory",
            script,
            StringComparison.Ordinal);

        var parameterBlock = script[..script.IndexOf(')')];
        Assert.DoesNotContain(
            "ApprovedWindowsRuntimeProps",
            parameterBlock,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SourceRevision", parameterBlock);
        Assert.DoesNotContain("ProductVersion", parameterBlock);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VR-Recorder.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
