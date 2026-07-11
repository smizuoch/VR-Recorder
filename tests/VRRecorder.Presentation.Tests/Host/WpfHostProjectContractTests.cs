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
    public void DesktopProductionFactoryRecoversConfiguredStaleRecordingsBeforeMediaPreflight()
    {
        var factory = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App",
            "ProductionDesktopRecordingRuntimeFactory.cs"));

        foreach (var productionType in new[]
                 {
                     "JsonFileSettingsStore",
                     "RecordingOutputPathResolver",
                     "WindowsDownloadsOutputPathProvider",
                     "StaleRecordingRecoveryUseCase",
                     "FileSystemStaleRecordingCatalog",
                     "FileSystemRecordingRecoveryStore",
                 })
        {
            Assert.Contains(productionType, factory);
        }

        Assert.Contains("settings.Recording.OutputFolder", factory);
        Assert.Contains("outputPath.FullPath", factory);
        var recovery = factory.IndexOf(
            "await RecoverStaleRecordingsAsync(",
            StringComparison.Ordinal);
        var mediaPreflight = factory.IndexOf(
            "File.Exists(nativeLibraryPath)",
            StringComparison.Ordinal);
        Assert.True(
            recovery >= 0,
            "Production stale-recording recovery is missing.");
        Assert.True(
            mediaPreflight > recovery,
            "Stale recordings must be quarantined before media preflight.");
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
                     "RotatingJsonLinesDiagnosticLog",
                     "StructuredRecordingEventSink",
                     "RecorderStatusDiagnosticObserver",
                     "JsonFileRecordingRightsAcknowledgementStore",
                     "RightsAcknowledgedDesktopRecordingStartRequestSource",
                 })
        {
            Assert.Contains(productionType, factory);
        }

        Assert.Contains("faultStops.Bind(sessions)", factory);
        Assert.DoesNotContain(
            "RECORDING_SERVICE_COMPOSITION_UNAVAILABLE",
            factory);
        Assert.DoesNotContain("ProductionRecordingEventSink", factory);
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
    public void DesktopHostRunsSteamVrInputOnlyAfterRecordingRuntimeIsReady()
    {
        var appCode = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App",
            "App.xaml.cs"));

        Assert.Contains("NativeSteamVrInputRuntime", appCode);
        Assert.Contains("SteamVrRecordingInputAdapter", appCode);
        Assert.Contains("DesktopRecordingHostState.Ready", appCode);
        Assert.Contains("StartSteamVrInput", appCode);
        Assert.Contains("AppContext.BaseDirectory", appCode);
        Assert.Contains("_recordingInputs", appCode);
        Assert.Contains("_steamVrInputLifetime.Cancel()", appCode);
        Assert.Contains("_steamVrInputTask", appCode);

        var readyCheck = appCode.IndexOf(
            "activation.State == DesktopRecordingHostState.Ready",
            StringComparison.Ordinal);
        var start = appCode.IndexOf("StartSteamVrInput", readyCheck, StringComparison.Ordinal);
        Assert.True(readyCheck >= 0, "SteamVR input is missing the host-ready gate.");
        Assert.True(start > readyCheck, "SteamVR input must start after the host-ready gate.");
    }

    [Fact]
    public void DesktopWindowCloseHidesToAnOperationalTrayMenu()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var project = XDocument.Load(Path.Combine(
            appDirectory,
            "VRRecorder.App.csproj"));
        Assert.Equal(
            "true",
            project.Descendants("UseWindowsForms").Single().Value);

        var window = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        Assert.Equal("OnClosing", window.Root?.Attribute("Closing")?.Value);
        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("App.IsExitRequested", windowCode);
        Assert.Contains("e.Cancel = true", windowCode);
        Assert.Contains("Hide()", windowCode);

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("NotifyIcon", appCode);
        Assert.Contains("ContextMenuStrip", appCode);
        Assert.Contains("InitializeTrayIcon", appCode);
        Assert.Contains("UiActivationKind.DesktopTray", appCode);
        Assert.Contains("OpenLegalWindow", appCode);
        Assert.Contains("OpenLicenseFolderAsync", appCode);
        Assert.Contains("Shutdown()", appCode);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "Tray_Show_Label",
                         "Tray_Toggle_Label",
                         "Tray_Legal_Label",
                         "Tray_LicenseFolder_Label",
                         "Tray_Exit_Label",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
    }

    [Fact]
    public void DesktopTrayTracksReadyRecordingWarningAndFaultStates()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));

        Assert.Contains("DesktopTrayUiController", appCode);
        Assert.Contains("_recordingHost.Subscribe", appCode);
        Assert.Contains("OnTrayStatusChanged", appCode);
        Assert.Contains("Dispatcher.CheckAccess()", appCode);
        Assert.Contains("_trayStatusMenuItem", appCode);
        Assert.Contains("_trayToggleMenuItem", appCode);
        Assert.Contains("update.StateLabelResourceKey", appCode);
        Assert.Contains("update.ActionLabelResourceKey", appCode);
        Assert.Contains("update.IsActionEnabled", appCode);
        Assert.Contains("_trayIcon.Text", appCode);
        Assert.Contains("_trayStatusSubscription.Dispose()", appCode);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "Tray_State_Ready",
                         "Tray_State_Recording",
                         "Tray_State_Warning",
                         "Tray_State_Fault",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
    }

    [Fact]
    public void DesktopSeparatelySurfacesSavedPathAndCameraRestoreWarnings()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var window = LoadRequiredXaml(appDirectory, "MainWindow.xaml");

        var saved = Assert.Single(
            window.Descendants(Presentation + "TextBlock"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "RecordingSavedText");
        Assert.Equal("Collapsed", saved.Attribute("Visibility")?.Value);
        Assert.Equal("Wrap", saved.Attribute("TextWrapping")?.Value);
        Assert.Equal(
            "Polite",
            saved.Attribute("AutomationProperties.LiveSetting")?.Value);

        var cameraWarning = Assert.Single(
            window.Descendants(Presentation + "TextBlock"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "CameraRestoreWarningText");
        Assert.Equal(
            "Collapsed",
            cameraWarning.Attribute("Visibility")?.Value);
        Assert.Equal("Wrap", cameraWarning.Attribute("TextWrapping")?.Value);
        Assert.Equal(
            "Assertive",
            cameraWarning.Attribute("AutomationProperties.LiveSetting")?.Value);

        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("DesktopRecordingNotificationHub", windowCode);
        Assert.Contains("_recordingNotificationSubscription", windowCode);
        Assert.Contains("DesktopRecordingNotification.Saved", windowCode);
        Assert.Contains("DesktopRecordingNotification.CameraWarning", windowCode);
        Assert.Contains("Recording.FinalPath", windowCode);
        Assert.Contains("Recording_Notification_Saved_Format", windowCode);
        Assert.Contains(
            "Recording_Notification_CameraRestoreWarning",
            windowCode);
        Assert.Contains("OnRecordingNotification", windowCode);
        Assert.Contains("Dispatcher.InvokeAsync", windowCode);
        Assert.Contains("ClearRecordingNotifications", windowCode);
        Assert.Contains("RecorderState.Arming", windowCode);
        Assert.Contains(
            "_recordingNotificationSubscription.Dispose()",
            windowCode);
        Assert.DoesNotContain("notification.Warning.Failure.Message", windowCode);

        var factoryCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "ProductionDesktopRecordingRuntimeFactory.cs"));
        Assert.Contains("DesktopRecordingNotificationHub", factoryCode);
        Assert.Contains("CompositeSavedRecordingSink", factoryCode);
        Assert.Contains("CompositeCameraRestoreWarningSink", factoryCode);

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("new DesktopRecordingNotificationHub()", appCode);
        Assert.Contains("RecordingNotifications", appCode);
        Assert.Contains("_trayNotificationSubscription", appCode);
        Assert.Contains("OnTrayRecordingNotification", appCode);
        Assert.Contains("ShowBalloonTip", appCode);
        var hostDisposal = appCode.IndexOf(
            "_recordingHost.DisposeAsync()",
            StringComparison.Ordinal);
        var notificationDisposal = appCode.IndexOf(
            "_recordingNotifications.Dispose()",
            StringComparison.Ordinal);
        Assert.True(hostDisposal >= 0, "Recording host disposal is missing.");
        Assert.True(
            notificationDisposal > hostDisposal,
            "Recording notifications must outlive recording-host shutdown.");

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "Recording_Notification_Saved_Title",
                         "Recording_Notification_Saved_Format",
                         "Recording_Notification_CameraRestoreWarning_Title",
                         "Recording_Notification_CameraRestoreWarning",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
    }

    [Fact]
    public void DesktopSurfacesAudioLossAndRecoveryWithoutFaultingRecording()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var window = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        var audio = Assert.Single(
            window.Descendants(Presentation + "TextBlock"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "AudioDeviceStatusText");
        Assert.Equal("Collapsed", audio.Attribute("Visibility")?.Value);
        Assert.Equal("Wrap", audio.Attribute("TextWrapping")?.Value);
        Assert.Equal(
            "Assertive",
            audio.Attribute("AutomationProperties.LiveSetting")?.Value);

        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("_unavailableAudioInputs", windowCode);
        Assert.Contains("DesktopRecordingNotification.AudioWarning", windowCode);
        Assert.Contains("DesktopRecordingNotification.AudioRecovered", windowCode);
        Assert.Contains("AudioInput.Desktop", windowCode);
        Assert.Contains("AudioInput.Microphone", windowCode);
        Assert.Contains("ApplyAudioAvailability", windowCode);
        Assert.Contains("AutomationLiveSetting.Assertive", windowCode);
        Assert.Contains("AutomationLiveSetting.Polite", windowCode);
        Assert.Contains(
            "Recording_Notification_Audio_BothUnavailable",
            windowCode);
        Assert.DoesNotContain("audio.Warning.Failure.Message", windowCode);

        var factoryCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "ProductionDesktopRecordingRuntimeFactory.cs"));
        Assert.Contains("CompositeAudioSessionEventSink", factoryCode);
        Assert.Contains("audioEvents", factoryCode);
        Assert.Contains(
            "new NativeRecordingEngine(",
            factoryCode);
        var audioComposition = factoryCode.IndexOf(
            "new CompositeAudioSessionEventSink(",
            StringComparison.Ordinal);
        var nativeEngine = factoryCode.IndexOf(
            "new NativeRecordingEngine(",
            StringComparison.Ordinal);
        Assert.True(
            audioComposition >= 0 && nativeEngine > audioComposition,
            "Audio diagnostics/presentation must be composed before the native engine.");

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("DesktopRecordingNotification.AudioWarning", appCode);
        Assert.Contains("DesktopRecordingNotification.AudioRecovered", appCode);
        Assert.Contains("Recording_Notification_Audio_Warning_Title", appCode);
        Assert.Contains("Recording_Notification_Audio_Recovered_Title", appCode);
        Assert.Contains("ShowBalloonTip", appCode);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "Recording_Notification_Audio_Warning_Title",
                         "Recording_Notification_Audio_Recovered_Title",
                         "Recording_Notification_Audio_DesktopUnavailable",
                         "Recording_Notification_Audio_MicrophoneUnavailable",
                         "Recording_Notification_Audio_BothUnavailable",
                         "Recording_Notification_Audio_DesktopRecovered",
                         "Recording_Notification_Audio_MicrophoneRecovered",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
    }

    [Fact]
    public void DesktopRequiresExplicitRecordingRightsAcknowledgementBeforeHostActivation()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var rightsWindow = LoadRequiredXaml(
            appDirectory,
            "RecordingRightsWindow.xaml");
        Assert.Equal(
            "VRRecorder.App.RecordingRightsWindow",
            rightsWindow.Root?.Attribute(Xaml + "Class")?.Value);
        var acknowledgement = Assert.Single(
            rightsWindow.Descendants(Presentation + "CheckBox"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "RightsAcknowledgementCheckBox");
        Assert.Equal(
            "OnAcknowledgementChanged",
            acknowledgement.Attribute("Checked")?.Value);
        Assert.Equal(
            "OnAcknowledgementChanged",
            acknowledgement.Attribute("Unchecked")?.Value);
        var accept = Assert.Single(
            rightsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "AcceptRightsButton");
        Assert.Equal("False", accept.Attribute("IsEnabled")?.Value);
        Assert.Equal("OnAccept", accept.Attribute("Click")?.Value);
        Assert.Single(
            rightsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute("Click")?.Value == "OnDecline");

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("JsonFileRecordingRightsAcknowledgementStore", appCode);
        Assert.Contains("RecordingRightsGate", appCode);
        Assert.Contains("IsRecordingRightsAcknowledgedAsync", appCode);
        Assert.Contains("AcknowledgeRecordingRightsAsync", appCode);

        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("RecordingRightsWindow", windowCode);
        Assert.Contains("ShowDialog()", windowCode);
        Assert.Contains("ExitAfterRightsDeclined", windowCode);
        var rightsCheck = windowCode.IndexOf(
            "IsRecordingRightsAcknowledgedAsync",
            StringComparison.Ordinal);
        var activation = windowCode.IndexOf(
            "ActivateRecordingHostAsync",
            StringComparison.Ordinal);
        Assert.True(rightsCheck >= 0, "The recording-rights gate is missing.");
        Assert.True(
            activation >= 0 && activation < rightsCheck,
            "Host initialization must recover a stale CameraLease before the rights dialog.");
        var applyStartup = windowCode.IndexOf(
            "ApplyStartupResult(startup, activation)",
            StringComparison.Ordinal);
        Assert.True(
            applyStartup > rightsCheck,
            "Recording controls must remain unavailable until rights acknowledgement.");

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "Rights_Title",
                         "Rights_Body",
                         "Rights_Acknowledge_Label",
                         "Rights_Accept_Label",
                         "Rights_Decline_Label",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
    }

    [Fact]
    public void DesktopKeepsRecordingCommandsDisabledUntilRightsGateCompletes()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var windowCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));

        Assert.Contains(
            "private bool _recordingCommandsAuthorized;",
            windowCode);
        Assert.Matches(
            @"RecordingToggleButton\.IsEnabled\s*=\s*" +
            @"isReady\s*&&\s*_recordingCommandsAuthorized",
            windowCode);
        Assert.Matches(
            @"RecordingToggleButton\.IsEnabled\s*=\s*" +
            @"_recordingCommandsAuthorized\s*&&\s*" +
            @"update\.IsActionEnabled",
            windowCode);

        var activation = windowCode.IndexOf(
            "ActivateRecordingHostAsync",
            StringComparison.Ordinal);
        var rightsCheck = windowCode.IndexOf(
            "IsRecordingRightsAcknowledgedAsync",
            StringComparison.Ordinal);
        var authorization = windowCode.IndexOf(
            "_recordingCommandsAuthorized = true;",
            StringComparison.Ordinal);
        Assert.True(
            authorization > activation && authorization > rightsCheck,
            "Recording commands must be authorized only after host recovery " +
            "and the recording-rights gate complete.");

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains(
            "private bool _recordingRightsAuthorized;",
            appCode);
        Assert.Matches(
            @"_trayToggleMenuItem\.Enabled\s*=\s*" +
            @"_recordingRightsAuthorized\s*&&\s*" +
            @"update\.IsActionEnabled",
            appCode);
        Assert.Contains("AuthorizeRecordingCommands", appCode);
        Assert.Contains("RefreshTrayStatus", appCode);
    }

    [Fact]
    public void DesktopPromptsForExactVrChatInstanceSelection()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var window = LoadRequiredXaml(
            appDirectory,
            "VrChatInstanceSelectionWindow.xaml");
        Assert.Equal(
            "VRRecorder.App.VrChatInstanceSelectionWindow",
            window.Root?.Attribute(Xaml + "Class")?.Value);

        var candidates = Assert.Single(
            window.Descendants(Presentation + "ListBox"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "VrChatInstanceList");
        Assert.Equal(
            "OnSelectionChanged",
            candidates.Attribute("SelectionChanged")?.Value);
        Assert.Equal(
            "{DynamicResource VrChat_Selection_List_AccessibleName}",
            candidates.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{DynamicResource VrChat_Selection_List_Tooltip}",
            candidates.Attribute("AutomationProperties.HelpText")?.Value);

        var accept = Assert.Single(
            window.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "AcceptSelectionButton");
        Assert.Equal("False", accept.Attribute("IsEnabled")?.Value);
        Assert.Equal("OnAccept", accept.Attribute("Click")?.Value);
        Assert.Single(
            window.Descendants(Presentation + "Button"),
            element => element.Attribute("Click")?.Value == "OnCancel");

        var promptCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "WpfVrChatInstanceSelectionPrompt.cs"));
        Assert.Contains("IVrChatInstanceSelectionPrompt", promptCode);
        Assert.Contains("Dispatcher", promptCode);
        Assert.Contains("ShowDialog()", promptCode);
        Assert.Contains("SelectedServiceId", promptCode);
        Assert.Contains("cancellationToken.Register", promptCode);

        var factory = File.ReadAllText(Path.Combine(
            appDirectory,
            "ProductionDesktopRecordingRuntimeFactory.cs"));
        Assert.Contains("new WpfVrChatInstanceSelectionPrompt", factory);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "VrChat_Selection_Title",
                         "VrChat_Selection_Body",
                         "VrChat_Selection_Item_Format",
                         "VrChat_Selection_List_AccessibleName",
                         "VrChat_Selection_List_Tooltip",
                         "VrChat_Selection_Accept_Label",
                         "VrChat_Selection_Accept_AccessibleName",
                         "VrChat_Selection_Accept_Tooltip",
                         "VrChat_Selection_Cancel_Label",
                         "VrChat_Selection_Cancel_AccessibleName",
                         "VrChat_Selection_Cancel_Tooltip",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
    }

    [Fact]
    public void DesktopSettingsWindowPersistsSupportedRecordingChoices()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var mainWindow = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        var settingsButton = Assert.Single(
            mainWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "SettingsButton");
        Assert.Equal(
            "OnSettingsClick",
            settingsButton.Attribute("Click")?.Value);

        var settingsWindow = LoadRequiredXaml(
            appDirectory,
            "SettingsWindow.xaml");
        Assert.Equal(
            "VRRecorder.App.SettingsWindow",
            settingsWindow.Root?.Attribute(Xaml + "Class")?.Value);
        var outputFolder = Assert.Single(
            settingsWindow.Descendants(Presentation + "TextBox"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "OutputFolderTextBox");
        Assert.Equal("True", outputFolder.Attribute("IsReadOnly")?.Value);
        Assert.NotNull(
            outputFolder.Attribute("AutomationProperties.Name"));
        Assert.NotNull(
            outputFolder.Attribute("AutomationProperties.HelpText"));
        var browseOutput = Assert.Single(
            settingsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "BrowseOutputFolderButton");
        Assert.Equal("False", browseOutput.Attribute("IsEnabled")?.Value);
        Assert.Equal("OnBrowseOutputFolder", browseOutput.Attribute("Click")?.Value);
        var useDownloads = Assert.Single(
            settingsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "UseDownloadsButton");
        Assert.Equal("False", useDownloads.Attribute("IsEnabled")?.Value);
        Assert.Equal("OnUseDownloads", useDownloads.Attribute("Click")?.Value);
        foreach (var comboName in new[]
                 {
                     "SelfTimerComboBox",
                     "AutoStopComboBox",
                     "FrameRateComboBox",
                     "ResolutionPolicyComboBox",
                     "EncoderComboBox",
                     "QualityPresetComboBox",
                     "AudioRoutingComboBox",
                 })
        {
            var combo = Assert.Single(
                settingsWindow.Descendants(Presentation + "ComboBox"),
                element => element.Attribute(Xaml + "Name")?.Value ==
                           comboName);
            Assert.Equal(
                "OnSelectionChanged",
                combo.Attribute("SelectionChanged")?.Value);
            Assert.NotNull(combo.Attribute("AutomationProperties.Name"));
            Assert.NotNull(combo.Attribute("AutomationProperties.HelpText"));
        }

        foreach (var sliderName in new[]
                 {
                     "DesktopGainSlider",
                     "MicrophoneGainSlider",
                 })
        {
            var slider = Assert.Single(
                settingsWindow.Descendants(Presentation + "Slider"),
                element => element.Attribute(Xaml + "Name")?.Value ==
                           sliderName);
            Assert.Equal("-96", slider.Attribute("Minimum")?.Value);
            Assert.Equal("24", slider.Attribute("Maximum")?.Value);
            Assert.Equal("OnGainChanged", slider.Attribute("ValueChanged")?.Value);
            Assert.NotNull(slider.Attribute("AutomationProperties.Name"));
            Assert.NotNull(slider.Attribute("AutomationProperties.HelpText"));
        }

        var rightsNotice = Assert.Single(
            settingsWindow.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                       "{DynamicResource Rights_Body}");
        Assert.Equal("Wrap", rightsNotice.Attribute("TextWrapping")?.Value);
        var save = Assert.Single(
            settingsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "SaveSettingsButton");
        Assert.Equal("False", save.Attribute("IsEnabled")?.Value);
        Assert.Equal("OnSave", save.Attribute("Click")?.Value);
        Assert.Single(
            settingsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute("Click")?.Value == "OnCancel");

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("JsonFileSettingsStore", appCode);
        Assert.Contains("DesktopRecordingSettingsController", appCode);
        Assert.Contains("RecordingSettings", appCode);
        var mainCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("SettingsWindow", mainCode);
        Assert.Contains("OpenSettingsWindow", mainCode);
        var settingsCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "SettingsWindow.xaml.cs"));
        Assert.Contains("SupportedSelfTimerSeconds", settingsCode);
        Assert.Contains("SupportedAutoStopSeconds", settingsCode);
        Assert.Contains("SupportedFrameRates", settingsCode);
        Assert.Contains("SupportedResolutionChangePolicies", settingsCode);
        Assert.Contains("SupportedEncoders", settingsCode);
        Assert.Contains("SupportedQualityPresets", settingsCode);
        Assert.Contains("SupportedAudioRoutings", settingsCode);
        Assert.Contains("AudioRouting =", settingsCode);
        Assert.Contains("DesktopGainDb =", settingsCode);
        Assert.Contains("MicrophoneGainDb =", settingsCode);
        Assert.Contains("FolderBrowserDialog", settingsCode);
        Assert.Contains("ResolveOutputPath", settingsCode);
        Assert.Contains("OutputFolder =", settingsCode);
        Assert.Contains("DownloadsKnownFolderToken", settingsCode);
        Assert.Contains("LoadAsync", settingsCode);
        Assert.Contains("SaveAsync", settingsCode);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "Settings_Open_Label",
                         "Settings_Open_AccessibleName",
                         "Settings_Open_Tooltip",
                         "Settings_Title",
                         "Settings_Intro",
                         "Settings_Rights_Heading",
                         "Settings_Output_Heading",
                         "Settings_OutputFolder_Label",
                         "Settings_OutputFolder_Tooltip",
                         "Settings_Output_Browse_Label",
                         "Settings_Output_Browse_AccessibleName",
                         "Settings_Output_Browse_Tooltip",
                         "Settings_Output_Browse_DialogTitle",
                         "Settings_Output_Downloads_Label",
                         "Settings_Output_Downloads_AccessibleName",
                         "Settings_Output_Downloads_Tooltip",
                         "Settings_SelfTimer_Label",
                         "Settings_AutoStop_Label",
                         "Settings_FrameRate_Label",
                         "Settings_ResolutionPolicy_Label",
                         "Settings_Encoder_Label",
                         "Settings_Quality_Label",
                         "Settings_Audio_Heading",
                         "Settings_AudioRouting_Label",
                         "Settings_AudioRouting_Tooltip",
                         "Settings_DesktopGain_Label",
                         "Settings_DesktopGain_Tooltip",
                         "Settings_MicrophoneGain_Label",
                         "Settings_MicrophoneGain_Tooltip",
                         "Settings_Audio_Mixed",
                         "Settings_Audio_DesktopOnly",
                         "Settings_Audio_MicOnly",
                         "Settings_Audio_Muted",
                         "Settings_Audio_Gain_Format",
                         "Settings_Save_Label",
                         "Settings_Cancel_Label",
                         "Settings_Load_Error",
                         "Settings_Save_Error",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
    }

    [Fact]
    public void DesktopExportsDiagnosticsOnlyThroughExplicitAccessibleAction()
    {
        var appDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.App");
        var mainWindow = LoadRequiredXaml(appDirectory, "MainWindow.xaml");
        var diagnosticsWindow = LoadRequiredXaml(
            appDirectory,
            "DiagnosticsWindow.xaml");

        var open = Assert.Single(
            mainWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "DiagnosticsButton");
        Assert.Equal(
            "{DynamicResource Diagnostics_Open_AccessibleName}",
            open.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{DynamicResource Diagnostics_Open_Tooltip}",
            open.Attribute("AutomationProperties.HelpText")?.Value);
        Assert.Equal(
            "{DynamicResource Diagnostics_Open_Tooltip}",
            open.Attribute("ToolTip")?.Value);
        Assert.Equal("OnDiagnosticsClick", open.Attribute("Click")?.Value);

        Assert.Equal(
            "VRRecorder.App.DiagnosticsWindow",
            diagnosticsWindow.Root?.Attribute(Xaml + "Class")?.Value);
        Assert.Null(diagnosticsWindow.Root?.Attribute("Loaded"));
        Assert.Equal(
            "{DynamicResource Layout.FlowDirection}",
            diagnosticsWindow.Root?.Attribute("FlowDirection")?.Value);
        Assert.Single(
            diagnosticsWindow.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                       "{DynamicResource Diagnostics_Privacy}");
        var export = Assert.Single(
            diagnosticsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "ExportDiagnosticsButton");
        Assert.Equal("OnExport", export.Attribute("Click")?.Value);
        Assert.NotNull(export.Attribute("AutomationProperties.Name"));
        Assert.NotNull(export.Attribute("AutomationProperties.HelpText"));
        var status = Assert.Single(
            diagnosticsWindow.Descendants(Presentation + "TextBlock"),
            element => element.Attribute(Xaml + "Name")?.Value ==
                       "DiagnosticsStatusText");
        Assert.Equal("Collapsed", status.Attribute("Visibility")?.Value);
        Assert.Equal(
            "Polite",
            status.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.Single(
            diagnosticsWindow.Descendants(Presentation + "Button"),
            element => element.Attribute("Click")?.Value == "OnClose");

        var diagnosticsCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "DiagnosticsWindow.xaml.cs"));
        Assert.Contains("DesktopDiagnosticsController", diagnosticsCode);
        Assert.Contains("SaveFileDialog", diagnosticsCode);
        Assert.Contains("ShowDialog()", diagnosticsCode);
        Assert.Contains("_controller.ExportAsync(", diagnosticsCode);
        Assert.Contains("LastExport?.BundlePath", diagnosticsCode);
        Assert.Contains("Diagnostics_Export_Success_Format", diagnosticsCode);
        Assert.Contains("Diagnostics_Export_Failure", diagnosticsCode);

        var mainCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("DiagnosticsWindow", mainCode);
        Assert.Contains("OpenDiagnosticsWindow", mainCode);
        Assert.Contains("diagnosticsWindow.Show();", mainCode);
        Assert.DoesNotContain("diagnosticsWindow.ShowDialog", mainCode);

        var appCode = File.ReadAllText(Path.Combine(
            appDirectory,
            "App.xaml.cs"));
        Assert.Contains("DesktopDiagnosticsController", appCode);
        Assert.Contains("PrivacySafeDiagnosticBundleExporter", appCode);
        Assert.Contains("DiagnosticsController", appCode);
        Assert.DoesNotContain(".ExportAsync(", appCode);

        foreach (var resourcePath in new[]
                 {
                     "Resources/Strings.en-US.xaml",
                     "Resources/Strings.ja-JP.xaml",
                     "Resources/Strings.qps-ploc.xaml",
                     "Resources/Strings.qps-plocm.xaml",
                 })
        {
            var resources = ReadStringResources(appDirectory, resourcePath);
            foreach (var key in new[]
                     {
                         "Diagnostics_Open_Label",
                         "Diagnostics_Open_AccessibleName",
                         "Diagnostics_Open_Tooltip",
                         "Diagnostics_Title",
                         "Diagnostics_Window_AccessibleName",
                         "Diagnostics_Intro",
                         "Diagnostics_Privacy",
                         "Diagnostics_Export_Label",
                         "Diagnostics_Export_AccessibleName",
                         "Diagnostics_Export_Tooltip",
                         "Diagnostics_Export_DialogTitle",
                         "Diagnostics_Export_Filter",
                         "Diagnostics_Export_DefaultFileName",
                         "Diagnostics_Exporting",
                         "Diagnostics_Export_Success_Format",
                         "Diagnostics_Export_Failure",
                         "Diagnostics_Close_Label",
                         "Diagnostics_Close_AccessibleName",
                         "Diagnostics_Close_Tooltip",
                     })
            {
                Assert.Contains(key, resources.Keys);
            }
        }
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
