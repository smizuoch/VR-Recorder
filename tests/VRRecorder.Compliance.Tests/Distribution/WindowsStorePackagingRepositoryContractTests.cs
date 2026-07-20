using System.Xml.Linq;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsStorePackagingRepositoryContractTests
{
    private static readonly XNamespace MsBuild =
        "http://schemas.microsoft.com/developer/msbuild/2003";
    private static readonly XNamespace Foundation =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap10 =
        "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace RestrictedCapabilities =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10/" +
        "restrictedcapabilities";

    [Fact]
    public void PackagingProjectIsSeparateAndCannotRebuildTheApplication()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "src",
            "VRRecorder.StorePackaging",
            "VRRecorder.StorePackaging.wapproj");
        var project = XDocument.Load(path);

        Assert.Empty(project.Descendants(MsBuild + "ProjectReference"));
        var manifest = Assert.Single(
            project.Descendants(MsBuild + "AppxManifest"));
        Assert.Equal("Package.appxmanifest",
            manifest.Attribute("Include")?.Value);
        Assert.Equal(
            "false",
            Assert.Single(project.Descendants(
                MsBuild + "AppxPackageSigningEnabled")).Value);
        Assert.Equal(
            "x64",
            Assert.Single(project.Descendants(
                MsBuild + "AppxBundlePlatforms")).Value);
    }

    [Fact]
    public void ManifestTemplateDeclaresTheApprovedDesktopTrustContract()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.StorePackaging",
            "Package.appxmanifest");
        var manifest = XDocument.Load(path);
        var identity = Assert.Single(
            manifest.Descendants(Foundation + "Identity"));
        Assert.Equal("x64",
            identity.Attribute("ProcessorArchitecture")?.Value);

        var deviceFamily = Assert.Single(
            manifest.Descendants(Foundation + "TargetDeviceFamily"));
        Assert.Equal("Windows.Desktop",
            deviceFamily.Attribute("Name")?.Value);
        Assert.Equal("10.0.19041.0",
            deviceFamily.Attribute("MinVersion")?.Value);

        var application = Assert.Single(
            manifest.Descendants(Foundation + "Application"));
        Assert.Equal("app\\VRRecorder.App.exe",
            application.Attribute("Executable")?.Value);
        Assert.Equal("packagedClassicApp",
            application.Attribute(Uap10 + "RuntimeBehavior")?.Value);
        Assert.Equal("mediumIL",
            application.Attribute(Uap10 + "TrustLevel")?.Value);

        var capability = Assert.Single(
            manifest.Descendants(
                RestrictedCapabilities + "Capability"));
        Assert.Equal("runFullTrust", capability.Attribute("Name")?.Value);
    }

    [Fact]
    public void BuildScriptPacksAndExpandsOnlyTheValidatedPayload()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "build-store-msix.ps1"));

        Assert.Contains("validate-store-packaging-input", script,
            StringComparison.Ordinal);
        Assert.Contains("& $makeAppx pack", script,
            StringComparison.Ordinal);
        Assert.Contains("& $makeAppx unpack", script,
            StringComparison.Ordinal);
        Assert.Contains("publishEligible = $false", script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", script,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VRRecorder.App.csproj", script,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowDownloadsOneImmutablePriorArtifactAndNoAppBuild()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "store-msix.yml"));

        Assert.Contains("actions: read", workflow,
            StringComparison.Ordinal);
        Assert.Contains("artifact-ids: ${{ inputs.validated_artifact_id }}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("run-id: ${{ inputs.validated_run_id }}", workflow,
            StringComparison.Ordinal);
        Assert.Contains("eng\\build-store-msix.ps1", workflow,
            StringComparison.Ordinal);
        Assert.Contains("*.store-packaging-identity.v1.json", workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", workflow,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet build VR-Recorder.sln", workflow,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "VR-Recorder.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
