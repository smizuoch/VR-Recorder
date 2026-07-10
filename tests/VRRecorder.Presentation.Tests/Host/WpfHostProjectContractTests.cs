using System.Xml.Linq;

namespace VRRecorder.Presentation.Tests.Host;

public sealed class WpfHostProjectContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

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

    [Fact]
    public void DesktopShellUsesLocalizedAccessibleSharedRecordingContract()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var app = LoadRequiredXaml(appDirectory, "App.xaml");
        var window = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        var codeBehindPath = Path.Combine(appDirectory, "MainWindow.xaml.cs");
        Assert.True(
            File.Exists(codeBehindPath),
            $"The desktop shell code-behind is missing: {codeBehindPath}");

        Assert.Equal("MainWindow.xaml", app.Root?.Attribute("StartupUri")?.Value);
        var mergedResources = app
            .Descendants(Presentation + "ResourceDictionary")
            .Select(dictionary => dictionary.Attribute("Source")?.Value)
            .Where(source => source is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Resources/DesignTokens.xaml", mergedResources);
        Assert.Contains("Resources/Strings.en-US.xaml", mergedResources);

        Assert.Equal(
            "VRRecorder.App.MainWindow",
            window.Root?.Attribute(Xaml + "Class")?.Value);
        var recordingButton = Assert.Single(
            window.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "RecordingToggleButton");
        Assert.Equal(
            "{DynamicResource Recording_Start_Short}",
            recordingButton.Attribute("Content")?.Value);
        Assert.Equal(
            "{DynamicResource Recording_Start_AccessibleName}",
            recordingButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{DynamicResource Recording_Start_Tooltip}",
            recordingButton.Attribute("ToolTip")?.Value);
        Assert.Equal(
            "{StaticResource Interaction.MinimumTarget}",
            recordingButton.Attribute("MinHeight")?.Value);
        Assert.Equal(
            "{StaticResource Interaction.MinimumTarget}",
            recordingButton.Attribute("MinWidth")?.Value);
        Assert.Equal(
            "OnRecordingToggleClick",
            recordingButton.Attribute("Click")?.Value);

        var status = Assert.Single(
            window.Descendants(Presentation + "TextBlock"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "RecordingStatusText");
        Assert.Equal(
            "{DynamicResource Recording_State_Booting}",
            status.Attribute("Text")?.Value);
        Assert.Equal(
            "{DynamicResource Status_Booting_AccessibleDescription}",
            status.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "Polite",
            status.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.Equal(
            "{StaticResource Spacing.LayoutGrid}",
            window.Descendants(Presentation + "Grid")
                .First()
                .Attribute("Margin")?.Value);
        Assert.Empty(window.Descendants(Presentation + "Image"));
        Assert.Empty(window.Descendants(Presentation + "Path"));

        var english = ReadStringResources(
            appDirectory,
            "Resources/Strings.en-US.xaml");
        var japanese = ReadStringResources(
            appDirectory,
            "Resources/Strings.ja-JP.xaml");
        Assert.Equal(english.Keys, japanese.Keys);
        AssertResources(
            english,
            ready: "Ready to record",
            startAccessibleName: "Start recording",
            stopAccessibleName: "Stop recording",
            readyDescription: "The connection and video signal are ready");
        AssertResources(
            japanese,
            ready: "録画準備完了",
            startAccessibleName: "録画を開始",
            stopAccessibleName: "録画を停止",
            readyDescription: "接続と映像信号は正常です");

        var codeBehind = File.ReadAllText(codeBehindPath);
        Assert.Contains("RecordingInputDispatcher", codeBehind);
        Assert.Contains("_recordingInputs.DispatchAsync(", codeBehind);
        Assert.Contains("UiActivationKind.DesktopClick", codeBehind);
        Assert.Contains("UiActivationKind.DesktopKeyboard", codeBehind);
        Assert.DoesNotContain("UiCommandId.ToggleRecording", codeBehind);

        var appCode = File.ReadAllText(Path.Combine(appDirectory, "App.xaml.cs"));
        Assert.Contains("Strings.en-US.xaml", appCode);
        Assert.Contains("Strings.ja-JP.xaml", appCode);
    }

    [Fact]
    public void DesktopShellFailsClosedUntilAuthenticatedLegalVerification()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "VRRecorder.App");
        var project = XDocument.Load(Path.Combine(
            appDirectory,
            "VRRecorder.App.csproj"));
        Assert.Contains(
            "../VRRecorder.Compliance/VRRecorder.Compliance.csproj",
            project.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value));
        var window = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        var recordingButton = Assert.Single(
            window.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "RecordingToggleButton");
        Assert.Equal("False", recordingButton.Attribute("IsEnabled")?.Value);
        var status = Assert.Single(
            window.Descendants(Presentation + "TextBlock"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "RecordingStatusText");
        Assert.Equal(
            "{DynamicResource Recording_State_Booting}",
            status.Attribute("Text")?.Value);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            Assert.Contains("Recording_State_Booting", resources.Keys);
            Assert.Contains("Recording_State_ComplianceFault", resources.Keys);
            Assert.Contains(
                "Status_ComplianceFault_AccessibleDescription",
                resources.Keys);
        }

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("RecorderStartupUseCase", appCode);
        Assert.Contains(
            "AssemblyMetadataAuthenticatedLegalBundleAnchorSource",
            appCode);
        Assert.Contains("RuntimeLegalBundleVerificationGateway", appCode);
        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("ApplyStartupResult", windowCode);
        Assert.Contains("RecorderState.ComplianceFault", windowCode);
    }

    private static XDocument LoadRequiredXaml(
        string appDirectory,
        string relativePath)
    {
        var path = Path.Combine(appDirectory, relativePath);
        Assert.True(File.Exists(path), $"Required WPF XAML is missing: {path}");
        return XDocument.Load(path);
    }

    private static SortedDictionary<string, string> ReadStringResources(
        string appDirectory,
        string relativePath)
    {
        var document = LoadRequiredXaml(appDirectory, relativePath);
        var resources = new SortedDictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var element in document.Root!.Elements())
        {
            var key = element.Attribute(Xaml + "Key")?.Value ??
                      throw new InvalidDataException(
                          $"A resource in {relativePath} has no x:Key.");
            resources.Add(key, element.Value);
        }

        return resources;
    }

    private static void AssertResources(
        SortedDictionary<string, string> resources,
        string ready,
        string startAccessibleName,
        string stopAccessibleName,
        string readyDescription)
    {
        Assert.Equal("VR-Recorder", resources["App_Title"]);
        Assert.Equal("REC", resources["Recording_Start_Short"]);
        Assert.Equal("STOP", resources["Recording_Stop_Short"]);
        Assert.Equal(ready, resources["Recording_State_Ready"]);
        Assert.Equal(
            startAccessibleName,
            resources["Recording_Start_AccessibleName"]);
        Assert.Equal(
            stopAccessibleName,
            resources["Recording_Stop_AccessibleName"]);
        Assert.Equal(
            readyDescription,
            resources["Status_Ready_AccessibleDescription"]);
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
