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
    public void DesktopShellSubscribesToRevisionedRuntimeStateOnDispatcher()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));

        Assert.Contains("IRecorderStatusSource", windowCode);
        Assert.Contains("DesktopRecordingUiController", windowCode);
        Assert.Contains(".Subscribe(", windowCode);
        Assert.Contains("Dispatcher.CheckAccess()", windowCode);
        Assert.Contains("Dispatcher.InvokeAsync", windowCode);
        Assert.Contains("ApplyRecordingStatus", windowCode);
        Assert.Contains("ContentControl.ContentProperty", windowCode);
        Assert.Contains("AutomationProperties.NameProperty", windowCode);
        Assert.Contains("AutomationProperties.HelpTextProperty", windowCode);
        Assert.Contains("FrameworkElement.ToolTipProperty", windowCode);
        Assert.Contains("RecordingStatuses", appCode);

        var requiredKeys = new[]
        {
            "Recording_Action_Cancel_Short",
            "Recording_Action_Cancel_AccessibleName",
            "Recording_Action_Cancel_Tooltip",
            "Recording_Action_Retry_Short",
            "Recording_Action_Retry_AccessibleName",
            "Recording_Action_Retry_Tooltip",
            "Recording_State_Arming",
            "Recording_State_Countdown",
            "Recording_State_Starting",
            "Recording_State_Recording",
            "Recording_State_SignalLost",
            "Recording_State_Stopping",
            "Recording_State_NoSignal",
            "Recording_State_Faulted",
            "Status_Arming_AccessibleDescription",
            "Status_Countdown_AccessibleDescription",
            "Status_Starting_AccessibleDescription",
            "Status_Recording_AccessibleDescription",
            "Status_SignalLost_AccessibleDescription",
            "Status_Stopping_AccessibleDescription",
            "Status_NoSignal_AccessibleDescription",
            "Status_Faulted_AccessibleDescription",
        };
        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            Assert.All(requiredKeys, key => Assert.Contains(key, resources.Keys));
        }
    }

    [Fact]
    public void DesktopPseudoLocaleAndRtlModesAreDeterministicAndOffline()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var english = ReadStringResources(
            appDirectory,
            "Resources/Strings.en-US.xaml");
        var japanese = ReadStringResources(
            appDirectory,
            "Resources/Strings.ja-JP.xaml");
        var pseudo = ReadStringResources(
            appDirectory,
            "Resources/Strings.qps-ploc.xaml");
        var mirroredPseudo = ReadStringResources(
            appDirectory,
            "Resources/Strings.qps-plocm.xaml");

        Assert.Equal(english.Keys, japanese.Keys);
        Assert.Equal(english.Keys, pseudo.Keys);
        Assert.Equal(english.Keys, mirroredPseudo.Keys);
        foreach (var (key, source) in english)
        {
            var expected = PseudoLocalize(source);
            Assert.Equal(expected, pseudo[key]);
            Assert.Equal(expected, mirroredPseudo[key]);
            Assert.True(
                pseudo[key].Length >= source.Length * 2,
                $"Pseudo-localized resource {key} is below 200% expansion.");
        }

        var app = LoadRequiredXaml(appDirectory, "App.xaml");
        var mergedSources = app
            .Descendants(Presentation + "ResourceDictionary")
            .Select(dictionary => dictionary.Attribute("Source")?.Value)
            .Where(source => source is not null)
            .ToArray();
        Assert.Contains("Resources/Layout.ltr.xaml", mergedSources);
        Assert.All(mergedSources, source => Assert.False(
            Uri.TryCreate(source, UriKind.Absolute, out _),
            $"WPF UI resource must be packaged offline: {source}"));

        var window = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        Assert.Equal(
            "{DynamicResource Layout.FlowDirection}",
            window.Root?.Attribute("FlowDirection")?.Value);
        Assert.Equal(
            "systemWindows:FlowDirection.LeftToRight",
            ReadStaticResourceMember(
                appDirectory,
                "Resources/Layout.ltr.xaml",
                "Layout.FlowDirection"));
        Assert.Equal(
            "systemWindows:FlowDirection.RightToLeft",
            ReadStaticResourceMember(
                appDirectory,
                "Resources/Layout.rtl.xaml",
                "Layout.FlowDirection"));

        var appCode = File.ReadAllText(Path.Combine(appDirectory, "App.xaml.cs"));
        Assert.Contains("--ui-locale=", appCode);
        Assert.Contains("qps-ploc", appCode);
        Assert.Contains("qps-plocm", appCode);
        Assert.Contains("Strings.qps-ploc.xaml", appCode);
        Assert.Contains("Strings.qps-plocm.xaml", appCode);
        Assert.Contains("Layout.ltr.xaml", appCode);
        Assert.Contains("Layout.rtl.xaml", appCode);
        Assert.False(File.Exists(Path.Combine(
            appDirectory,
            "Resources",
            "Strings.ar.xaml")));
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
        Assert.Equal(
            3,
            appCode.Split(
                "LegalBundleVerificationScope.InstallRoot",
                StringSplitOptions.None).Length - 1);
        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("ApplyStartupResult", windowCode);
        Assert.Contains("RecorderState.ComplianceFault", windowCode);
    }

    [Fact]
    public void DesktopShellActivatesRecordingHostAndSurfacesServiceFailure()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("DesktopRecordingCommandHost", appCode);
        Assert.Contains("RecordingUiCommandDispatcher", appCode);
        Assert.Contains("_recordingHost.ActivateAsync(", appCode);
        Assert.Contains("ProductionDesktopRecordingRuntimeFactory", appCode);
        Assert.DoesNotContain("UnavailableUiCommandDispatcher", appCode);

        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("DesktopRecordingHostActivation", windowCode);
        Assert.Contains(
            "DesktopRecordingHostState.InitializationFailed",
            windowCode);
        Assert.Contains("Recording_State_InitializationFailed", windowCode);
        Assert.Contains(
            "Status_InitializationFailed_AccessibleDescription",
            windowCode);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            Assert.Contains(
                "Recording_State_InitializationFailed",
                resources.Keys);
            Assert.Contains(
                "Status_InitializationFailed_AccessibleDescription",
                resources.Keys);
        }
    }

    [Fact]
    public void DesktopProductionFactoryRecoversStaleCameraLeaseBeforeMediaPreflight()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var project = XDocument.Load(Path.Combine(
            appDirectory,
            "VRRecorder.App.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            "../VRRecorder.Infrastructure.Osc/VRRecorder.Infrastructure.Osc.csproj",
            references);
        Assert.Contains(
            "../VRRecorder.Infrastructure.Storage/VRRecorder.Infrastructure.Storage.csproj",
            references);

        var factory = File.ReadAllText(Path.Combine(
            appDirectory,
            "ProductionDesktopRecordingRuntimeFactory.cs"));
        foreach (var productionType in new[]
                 {
                     "WindowsSettingsPathProvider",
                     "FileSystemCameraLeaseStore",
                     "SystemProcessCameraLeaseOwnerActivityProbe",
                     "WindowsDnsSdOscQueryServiceBrowser",
                     "OscQueryVrChatInstanceDiscovery",
                     "ConfirmedUdpVrChatCameraGatewayFactory",
                     "VrChatTargetResolver",
                     "VrChatCameraConnectionUseCase",
                     "StaleCameraLeaseRecoveryUseCase",
                 })
        {
            Assert.Contains(productionType, factory);
        }

        Assert.Contains("StaleCameraLeaseRecoveryResult.NoLease", factory);
        Assert.Contains("StaleCameraLeaseRecoveryResult.Restored", factory);
        Assert.Contains("StaleCameraLeaseRecoveryResult.OwnerStillActive", factory);
        Assert.Contains("StaleCameraLeaseRecoveryResult.Failed", factory);
        Assert.Contains("DesktopRecordingInitializationException", factory);
        Assert.DoesNotContain("NoOpCameraRestoreWarningSink", factory);

        var recovery = factory.IndexOf(
            "await RecoverStaleCameraLeaseAsync(",
            StringComparison.Ordinal);
        var mediaPreflight = factory.IndexOf(
            "File.Exists(nativeLibraryPath)",
            StringComparison.Ordinal);
        Assert.True(recovery >= 0, "Production stale CameraLease recovery is missing.");
        Assert.True(
            mediaPreflight > recovery,
            "Stale CameraLease recovery must finish before media preflight.");
    }

    [Fact]
    public void DesktopProductionFactoryComposesConcreteRecordingRuntime()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var project = XDocument.Load(Path.Combine(
            appDirectory,
            "VRRecorder.App.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            "../VRRecorder.Infrastructure.Media/VRRecorder.Infrastructure.Media.csproj",
            references);

        var factory = File.ReadAllText(Path.Combine(
            appDirectory,
            "ProductionDesktopRecordingRuntimeFactory.cs"));
        foreach (var productionType in new[]
                 {
                     "PInvokeSpoutVideoSource",
                     "PInvokeEncoderProbe",
                     "PInvokeNativeRecordingBackend",
                     "NativeRecordingFaultStopSink",
                     "NativeRecordingEngine",
                     "ActiveRecordingSessionCoordinator",
                     "RecordingStorageMonitor",
                     "StartRecordingUseCase",
                     "RecordingLifecycleController",
                     "DesktopRecordingRuntime",
                     "RecordingRuntimeResourceLifetime",
                     "LegalBundleMirroringDesktopRecordingStartRequestSource",
                     "AuthenticatedLegalBundleOutputMirror",
                     "FfprobeRecordingFileValidator",
                 })
        {
            Assert.Contains(productionType, factory);
        }

        Assert.Contains("faultStops.Bind(sessions)", factory);
        Assert.DoesNotContain(
            "RECORDING_SERVICE_COMPOSITION_UNAVAILABLE",
            factory);
    }

    [Fact]
    public void DesktopPublishRequiresAndCopiesApprovedMediaRuntimeInputs()
    {
        var project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App",
            "VRRecorder.App.csproj"));

        var native = Assert.Single(project.Descendants("Content"), element =>
            element.Attribute("Include")?.Value == "$(NativeMediaLibraryPath)");
        Assert.Equal("vrrecorder_native.dll", native.Element("Link")?.Value);
        Assert.Equal("PreserveNewest", native.Element("CopyToOutputDirectory")?.Value);
        Assert.Equal("PreserveNewest", native.Element("CopyToPublishDirectory")?.Value);

        var ffprobe = Assert.Single(project.Descendants("Content"), element =>
            element.Attribute("Include")?.Value == "$(FfprobeExecutablePath)");
        Assert.Equal("ffprobe.exe", ffprobe.Element("Link")?.Value);
        Assert.Equal("PreserveNewest", ffprobe.Element("CopyToOutputDirectory")?.Value);
        Assert.Equal("PreserveNewest", ffprobe.Element("CopyToPublishDirectory")?.Value);

        var validation = Assert.Single(project.Descendants("Target"), element =>
            element.Attribute("Name")?.Value == "ValidateReleaseMediaRuntime");
        Assert.Equal(
            "PrepareForBuild",
            validation.Attribute("BeforeTargets")?.Value);
        Assert.Contains("'$(Configuration)' == 'Release'", validation.Attribute("Condition")?.Value);
        var errors = validation.Elements("Error")
            .Select(error => error.Attribute("Condition")?.Value ?? string.Empty)
            .ToArray();
        Assert.Contains(errors, condition =>
            condition.Contains("NativeMediaLibraryPath", StringComparison.Ordinal));
        Assert.Contains(errors, condition =>
            condition.Contains("FfprobeExecutablePath", StringComparison.Ordinal));
    }

    [Fact]
    public void DesktopAboutAndLegalIsAccessibleExpansionSafeAndModeless()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var mainWindow = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        var legalWindow = LoadRequiredXaml(appDirectory, "LegalWindow.xaml");

        var aboutButton = Assert.Single(
            mainWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "AboutLegalButton");
        Assert.Equal(
            "{DynamicResource Legal_Open_AccessibleName}",
            aboutButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{DynamicResource Legal_Open_Tooltip}",
            aboutButton.Attribute("ToolTip")?.Value);
        Assert.Equal("OnAboutLegalClick", aboutButton.Attribute("Click")?.Value);

        Assert.Equal(
            "VRRecorder.App.LegalWindow",
            legalWindow.Root?.Attribute(Xaml + "Class")?.Value);
        Assert.Equal(
            "{DynamicResource Layout.FlowDirection}",
            legalWindow.Root?.Attribute("FlowDirection")?.Value);
        var scrollViewer = Assert.Single(
            legalWindow.Descendants(Presentation + "ScrollViewer"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "LegalContentScrollViewer");
        Assert.Equal(
            "Auto",
            scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Auto",
            scrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);

        foreach (var name in new[]
                 {
                     "LegalComponentList",
                     "LegalDocumentList",
                     "FullDocumentText",
                     "OpenLicenseFolderButton",
                     "RefreshLegalButton",
                     "CloseLegalButton",
                 })
        {
            var control = Assert.Single(
                legalWindow.Descendants(),
                element => element.Attribute(Xaml + "Name")?.Value == name);
            Assert.NotNull(control.Attribute("AutomationProperties.Name"));
            Assert.NotNull(control.Attribute("ToolTip"));
        }

        var identityFields = legalWindow
            .Descendants(Presentation + "TextBlock")
            .Select(element => element.Attribute(Xaml + "Name")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ProductVersionText", identityFields);
        Assert.Contains("BundleIdentityText", identityFields);
        Assert.Contains("ManifestSha256Text", identityFields);
        Assert.Contains("ComponentDetailText", identityFields);
        Assert.Contains("LegalDocumentHeadingText", identityFields);
        Assert.Contains("LegalUnavailableText", identityFields);
        Assert.Single(
            legalWindow.Descendants(Presentation + "TextBox"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "FullDocumentText");

        var mainCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("new LegalWindow", mainCode);
        Assert.Contains("legalWindow.Show();", mainCode);
        Assert.DoesNotContain("legalWindow.ShowDialog", mainCode);
        var legalCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "LegalWindow.xaml.cs"));
        Assert.Contains("DesktopLegalController", legalCode);
        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("_recordingHost);", appCode);
        Assert.Contains("RunLegalOperationAsync", legalCode);
        Assert.Contains("catch (Exception)", legalCode);
        Assert.DoesNotContain("RecordingInputDispatcher", legalCode);
        Assert.DoesNotContain("UiCommandId.ToggleRecording", legalCode);
        Assert.DoesNotContain("HttpClient", legalCode);

        var english = ReadStringResources(
            appDirectory,
            "Resources/Strings.en-US.xaml");
        foreach (var key in new[]
                 {
                     "Legal_Title",
                     "Legal_ProductVersion_Label",
                     "Legal_BundleIdentity_Label",
                     "Legal_ManifestSha256_Label",
                     "Legal_ThirdPartyComponents",
                     "Legal_Documents_Header",
                     "Legal_DocumentText_Heading",
                     "Legal_OpenFolder_AccessibleName",
                     "Legal_OpenFolder_Tooltip",
                     "Legal_State_ComplianceFault",
                 })
        {
            Assert.Contains(key, english.Keys);
        }
    }

    [Fact]
    public void DesktopLegalWindowProjectsEveryAuthenticatedV3Document()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var legalWindow = LoadRequiredXaml(appDirectory, "LegalWindow.xaml");

        var manifest = Assert.Single(
            legalWindow.Descendants(Presentation + "TextBlock"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "ManifestSha256Text");
        Assert.Null(manifest.Attribute("AutomationProperties.Name"));
        Assert.Equal("Wrap", manifest.Attribute("TextWrapping")?.Value);

        var documentList = Assert.Single(
            legalWindow.Descendants(Presentation + "ListBox"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "LegalDocumentList");
        Assert.Equal(
            "OnDocumentSelectionChanged",
            documentList.Attribute("SelectionChanged")?.Value);
        Assert.Equal(
            "{DynamicResource Legal_DocumentList_AccessibleName}",
            documentList.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{DynamicResource Legal_DocumentList_Tooltip}",
            documentList.Attribute("AutomationProperties.HelpText")?.Value);
        Assert.Equal(
            "{DynamicResource Legal_DocumentList_Tooltip}",
            documentList.Attribute("ToolTip")?.Value);

        var documentText = Assert.Single(
            legalWindow.Descendants(Presentation + "TextBox"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "FullDocumentText");
        Assert.Equal("True", documentText.Attribute("IsReadOnly")?.Value);
        Assert.Equal(
            "{DynamicResource Legal_DocumentText_AccessibleName}",
            documentText.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{DynamicResource Legal_DocumentText_Tooltip}",
            documentText.Attribute("AutomationProperties.HelpText")?.Value);
        Assert.Equal(
            "{DynamicResource Legal_DocumentText_Tooltip}",
            documentText.Attribute("ToolTip")?.Value);

        var legalCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "LegalWindow.xaml.cs"));
        Assert.Contains("_controller.ShowDocumentAsync(", legalCode);
        Assert.Contains("selected.Reference", legalCode);
        Assert.Contains("state.ManifestSha256", legalCode);
        Assert.Contains("state.FullDocumentText", legalCode);
        Assert.Contains("component.CopyrightNotice", legalCode);
        Assert.Contains("Legal_Detail_Copyright_Format", legalCode);
        Assert.Contains("LegalDocumentList.ItemsSource =", legalCode);
        Assert.Contains("LegalDocumentList.SelectedItem =", legalCode);
        Assert.Contains("FullDocumentText.Text =", legalCode);
        Assert.Contains("AutomationProperties.SetName", legalCode);
        Assert.Contains(
            "available ? state.ManifestSha256 : null",
            legalCode);
        Assert.Contains(
            "available ? state.FullDocumentText : null",
            legalCode);

        var requiredKeys = new[]
        {
            "Legal_ManifestSha256_Label",
            "Legal_ManifestSha256_AccessibleName",
            "Legal_ManifestSha256_Format",
            "Legal_Detail_Copyright_Format",
            "Legal_Documents_Header",
            "Legal_DocumentList_AccessibleName",
            "Legal_DocumentList_Tooltip",
            "Legal_DocumentKind_License",
            "Legal_DocumentKind_Notice",
            "Legal_DocumentKind_Copyright",
            "Legal_DocumentKind_Attribution",
            "Legal_DocumentKind_AssetManifest",
            "Legal_DocumentText_Heading",
            "Legal_DocumentText_HeadingFormat",
            "Legal_DocumentText_AccessibleName",
            "Legal_DocumentText_AccessibleNameFormat",
            "Legal_DocumentText_Tooltip",
        };
        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            Assert.All(requiredKeys, key =>
                Assert.Contains(key, resources.Keys));
        }
    }

    [Fact]
    public void DesktopLegalDynamicValuesAndTypographyUseSemanticContracts()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var legalWindow = LoadRequiredXaml(appDirectory, "LegalWindow.xaml");

        foreach (var name in new[]
                 {
                     "ProductVersionText",
                     "BundleIdentityText",
                     "ManifestSha256Text",
                     "ComponentDetailText",
                     "LegalDocumentHeadingText",
                 })
        {
            var dynamicValue = Assert.Single(
                legalWindow.Descendants(Presentation + "TextBlock"),
                element => element.Attribute(Xaml + "Name")?.Value == name);
            Assert.Null(dynamicValue.Attribute("AutomationProperties.Name"));
        }

        var legalCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "LegalWindow.xaml.cs"));
        Assert.Contains(
            "ApplyAccessibleText(ProductVersionText,",
            legalCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyAccessibleText(BundleIdentityText,",
            legalCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyAccessibleText(ComponentDetailText,",
            legalCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyAccessibleText(ManifestSha256Text,",
            legalCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyAccessibleText(LegalDocumentHeadingText,",
            legalCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.SetName(target, semantic.AutomationName);",
            legalCode,
            StringComparison.Ordinal);

        Assert.All(
            legalWindow.Descendants()
                .Select(element => element.Attribute("FontSize")?.Value)
                .Where(value => value is not null),
            value => Assert.StartsWith("{", value, StringComparison.Ordinal));
        var heading = Assert.Single(
            legalWindow.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                       "{DynamicResource Legal_Title}");
        Assert.Equal(
            "{StaticResource Typography.HeadlineMedium.FontSize}",
            heading.Attribute("FontSize")?.Value);

        var tokens = LoadRequiredXaml(
            appDirectory,
            "Resources/DesignTokens.xaml");
        var typographyToken = Assert.Single(
            tokens.Root!.Elements(),
            element => element.Attribute(Xaml + "Key")?.Value ==
                       "Typography.HeadlineMedium.FontSize");
        Assert.Equal("22", typographyToken.Value);
    }

    [Fact]
    public void ReleaseBuildRequiresAuthenticatedLegalAnchorAndPayload()
    {
        var project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App",
            "VRRecorder.App.csproj"));
        var metadata = project
            .Descendants("AssemblyMetadata")
            .ToDictionary(
                item => item.Attribute("Include")?.Value!,
                item => item.Attribute("Value")?.Value!,
                StringComparer.Ordinal);
        Assert.Equal(
            "$(LegalBundleId)",
            metadata["VRRecorder.LegalBundleId"]);
        Assert.Equal(
            "$(LegalManifestSha256)",
            metadata["VRRecorder.LegalManifestSha256"]);

        var legalPayload = Assert.Single(project.Descendants("Content"), item =>
            item.Attribute("Include")?.Value ==
            "$(LegalBundleDirectory)/**/*");
        Assert.Equal(
            "PreserveNewest",
            legalPayload.Attribute("CopyToOutputDirectory")?.Value);
        Assert.Equal(
            "PreserveNewest",
            legalPayload.Attribute("CopyToPublishDirectory")?.Value);

        var gate = Assert.Single(project.Descendants("Target"), target =>
            target.Attribute("Name")?.Value == "ValidateReleaseLegalBundle");
        Assert.Equal("PrepareForBuild", gate.Attribute("BeforeTargets")?.Value);
        Assert.Contains(
            "$(Configuration)",
            gate.Attribute("Condition")?.Value,
            StringComparison.Ordinal);
        var errors = gate
            .Elements("Error")
            .Select(error => error.Attribute("Text")?.Value)
            .Where(text => text is not null)
            .ToArray();
        Assert.Contains(errors, text =>
            text!.Contains("LegalBundleId", StringComparison.Ordinal));
        Assert.Contains(errors, text =>
            text!.Contains("LegalManifestSha256", StringComparison.Ordinal));
        Assert.Contains(errors, text =>
            text!.Contains("LEGAL-MANIFEST.sha256", StringComparison.Ordinal));
        var manifestHash = Assert.Single(gate.Elements("GetFileHash"));
        Assert.Equal(
            "$(LegalBundleDirectory)/LEGAL-MANIFEST.sha256",
            manifestHash.Attribute("Files")?.Value);
        Assert.Equal("SHA256", manifestHash.Attribute("Algorithm")?.Value);
        var hashOutput = Assert.Single(manifestHash.Elements("Output"));
        Assert.Equal("Items", hashOutput.Attribute("TaskParameter")?.Value);
        Assert.Equal(
            "_LegalManifestHash",
            hashOutput.Attribute("ItemName")?.Value);
        Assert.Contains(errors, text =>
            text!.Contains("digest does not match", StringComparison.Ordinal));
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

    private static string ReadStaticResourceMember(
        string appDirectory,
        string relativePath,
        string resourceKey)
    {
        var document = LoadRequiredXaml(appDirectory, relativePath);
        var resource = Assert.Single(document.Root!.Elements(), element =>
            element.Attribute(Xaml + "Key")?.Value == resourceKey);
        return resource.Attribute("Member")?.Value ??
               throw new InvalidDataException(
                   $"Resource {resourceKey} has no x:Static Member.");
    }

    private static string PseudoLocalize(string source)
    {
        var transformed = string.Concat(source.Select(character =>
            character switch
            {
                'A' => 'Á',
                'E' => 'Ë',
                'I' => 'Ï',
                'O' => 'Ö',
                'U' => 'Ü',
                'a' => 'á',
                'e' => 'ë',
                'i' => 'ï',
                'o' => 'ö',
                'u' => 'ü',
                _ => character,
            }));
        return $"⟦{transformed} · {transformed}⟧";
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
