namespace VRRecorder.Compliance.Tests.Repository;

public sealed class WindowsHardwareValidationPublishScriptContractTests
{
    [Fact]
    public void PreparationScriptPinsTheCompleteFullProductionRuntimeClosure()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "prepare-windows-runtime-input.ps1"));

        foreach (var required in new[]
                 {
                     "vrrecorder_native.dll",
                     "native-factory-selection.json",
                     "avcodec-62.dll",
                     "avformat-62.dll",
                     "avutil-60.dll",
                     "swresample-6.dll",
                     "libvpl.dll",
                     "ffprobe.exe",
                     "openvr_api.dll",
                     "msvcp140.dll",
                     "msvcp140_atomic_wait.dll",
                     "vcruntime140.dll",
                     "vcruntime140_1.dll",
                     "steamvr.vrmanifest",
                     "actions.json",
                     "knuckles.json",
                     "oculus_touch.json",
                     "vive_controller.json",
                 })
        {
            Assert.Contains(required, script, StringComparison.Ordinal);
        }

        Assert.Contains("schemaVersion = 2", script, StringComparison.Ordinal);
        Assert.Contains("full-production-hardware-validation-v1", script,
            StringComparison.Ordinal);
        Assert.Contains("LEGAL-MANIFEST.sha256", script,
            StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", script, StringComparison.Ordinal);
        Assert.Contains("OutputDirectory must not already exist", script,
            StringComparison.Ordinal);
    }

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
            "VRRecorderUseWinX64LockGraph=true",
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
