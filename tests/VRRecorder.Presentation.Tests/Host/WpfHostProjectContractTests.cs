using System.Xml.Linq;

namespace VRRecorder.Presentation.Tests.Host;

public sealed class WpfHostProjectContractTests
{
    [Fact]
    public void WindowsHostHasRequiredBuildContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "VRRecorder.App",
            "VRRecorder.App.csproj");

        Assert.True(
            File.Exists(projectPath),
            $"The WPF host project is missing: {projectPath}");

        var project = XDocument.Load(projectPath).Root;
        Assert.NotNull(project);
        Assert.Equal("Microsoft.NET.Sdk", project.Attribute("Sdk")?.Value);
        AssertProperty(project, "OutputType", "WinExe");
        AssertProperty(
            project,
            "TargetFramework",
            "net10.0-windows10.0.19041.0");
        AssertProperty(project, "RuntimeIdentifier", "win-x64");
        AssertProperty(project, "PlatformTarget", "x64");
        AssertProperty(project, "UseWPF", "true");
        AssertProperty(project, "EnableWindowsTargeting", "true");
        AssertProperty(project, "PublishSelfContained", "true");
        AssertProperty(project, "RootNamespace", "VRRecorder.App");

        var references = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            "../VRRecorder.Application/VRRecorder.Application.csproj",
            references);
        Assert.Contains(
            "../VRRecorder.DesignSystem/VRRecorder.DesignSystem.csproj",
            references);
        Assert.Contains(
            "../VRRecorder.Infrastructure.SteamVr/VRRecorder.Infrastructure.SteamVr.csproj",
            references);

        var steamVrProjectPath = Path.Combine(
            repositoryRoot,
            "src",
            "VRRecorder.Infrastructure.SteamVr",
            "VRRecorder.Infrastructure.SteamVr.csproj");
        var steamVrProject = XDocument.Load(steamVrProjectPath).Root;
        Assert.NotNull(steamVrProject);
        var openVrPayload = Assert.Single(steamVrProject
            .Descendants("None"),
            item => item.Attribute("Update")?.Value == "OpenVr/**/*.json");
        Assert.Equal(
            "PreserveNewest",
            openVrPayload.Attribute("CopyToOutputDirectory")?.Value);
        Assert.Equal(
            "PreserveNewest",
            openVrPayload.Attribute("CopyToPublishDirectory")?.Value);
    }

    private static void AssertProperty(
        XElement project,
        string name,
        string expected)
    {
        var property = project.Descendants(name).SingleOrDefault();
        Assert.NotNull(property);
        Assert.Equal(expected, property.Value);
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
            "The VR-Recorder repository root was not found.");
    }
}
