using System.Runtime.CompilerServices;
using System.Text.Json;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class SteamVrRecordingInputAdapterTests
{
    private static readonly string[] ExpectedControllerTypes =
        ["knuckles", "oculus_touch", "vive_controller"];
    private static readonly Dictionary<string,
        (string Microphone, string Recenter)> ExpectedPlacementBindings =
        new Dictionary<string, (string Microphone, string Recenter)>(
            StringComparer.Ordinal)
        {
            ["knuckles"] = (
                "/user/hand/left/input/b",
                "/user/hand/right/input/b"),
            ["oculus_touch"] = (
                "/user/hand/left/input/y",
                "/user/hand/right/input/b"),
            ["vive_controller"] = (
                "/user/hand/left/input/grip",
                "/user/hand/right/input/grip"),
        };

    [Fact]
    public async Task RecenterActionInvokesPlacementOnlyOnActiveRisingEdges()
    {
        var runtime = new ScriptedSteamVrInputRuntime(
        [
            new SteamVrDigitalActionState(
                IsActive: false,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: false,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
        ]);
        var placement = new CapturingPlacementCommands();
        var adapter = new SteamVrOverlayPlacementInputAdapter(
            runtime,
            placement);

        await adapter.RunAsync(CancellationToken.None);

        Assert.Equal(
            WristOverlayInputContract.SteamVrRecenterActionPath,
            runtime.RequestedActionPath);
        Assert.Equal(2, placement.RecenterCount);
    }

    [Fact]
    public async Task MicrophoneActionDispatchesOnlyActiveRisingEdges()
    {
        var runtime = new ScriptedSteamVrInputRuntime(
        [
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: false,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
        ]);
        var commands = new CapturingUiCommandDispatcher();
        var adapter = new SteamVrMicrophoneInputAdapter(runtime, commands);

        await adapter.RunAsync(CancellationToken.None);

        Assert.Equal(
            RecordingInputContract.SteamVrToggleMicrophoneActionPath,
            runtime.RequestedActionPath);
        Assert.Equal(2, commands.Commands.Count);
        Assert.All(commands.Commands, command =>
        {
            Assert.Equal(UiCommandId.ToggleMicrophone, command.Command);
            Assert.Equal(UiActivationKind.SteamVrAction, command.ActivationKind);
        });
    }

    [Fact]
    public async Task RecordingPollingContinuesAfterARejectedCommand()
    {
        var runtime = TwoPressesWithRelease();
        var commands = new RejectingFirstUiCommandDispatcher();
        var adapter = new SteamVrRecordingInputAdapter(
            runtime,
            new RecordingInputDispatcher(commands));

        await adapter.RunAsync(CancellationToken.None);

        Assert.Equal(2, commands.Attempts.Count);
        Assert.All(commands.Attempts, command => Assert.Equal(
            UiCommandId.ToggleRecording,
            command.Command));
        Assert.Single(commands.Completed);
    }

    [Fact]
    public async Task MicrophonePollingContinuesAfterARejectedCommand()
    {
        var runtime = TwoPressesWithRelease();
        var commands = new RejectingFirstUiCommandDispatcher();
        var adapter = new SteamVrMicrophoneInputAdapter(runtime, commands);

        await adapter.RunAsync(CancellationToken.None);

        Assert.Equal(2, commands.Attempts.Count);
        Assert.All(commands.Attempts, command => Assert.Equal(
            UiCommandId.ToggleMicrophone,
            command.Command));
        Assert.Single(commands.Completed);
    }

    [Fact]
    public async Task DispatchesCanonicalToggleOnlyForActiveRisingEdges()
    {
        var runtime = new ScriptedSteamVrInputRuntime(
        [
            new SteamVrDigitalActionState(
                IsActive: false,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: false),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: false,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
        ]);
        var commands = new CapturingUiCommandDispatcher();
        var adapter = new SteamVrRecordingInputAdapter(
            runtime,
            new RecordingInputDispatcher(commands));

        await adapter.RunAsync(CancellationToken.None);

        Assert.Equal(
            RecordingInputContract.SteamVrToggleActionPath,
            runtime.RequestedActionPath);
        Assert.Equal(2, commands.Commands.Count);
        Assert.All(commands.Commands, command =>
        {
            Assert.Equal(UiCommandId.ToggleRecording, command.Command);
            Assert.Equal(UiActivationKind.SteamVrAction, command.ActivationKind);
        });
    }

    [Fact]
    public async Task RepeatedChangedPressesWaitForReleaseOrInactiveBeforeRedispatch()
    {
        var runtime = new ScriptedSteamVrInputRuntime(
        [
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: false,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: false,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
        ]);
        var commands = new CapturingUiCommandDispatcher();
        var adapter = new SteamVrRecordingInputAdapter(
            runtime,
            new RecordingInputDispatcher(commands));

        await adapter.RunAsync(CancellationToken.None);

        Assert.Equal(3, commands.Commands.Count);
        Assert.All(commands.Commands, command =>
        {
            Assert.Equal(UiCommandId.ToggleRecording, command.Command);
            Assert.Equal(UiActivationKind.SteamVrAction, command.ActivationKind);
        });
    }

    [Fact]
    public void ActionManifestDefinesLocalizedBooleanToggleAndBindings()
    {
        var openVrDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.Infrastructure.SteamVr",
            "OpenVr");
        var manifestPath = Path.Combine(openVrDirectory, "actions.json");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = manifest.RootElement;
        var toggle = Assert.Single(
            root.GetProperty("actions").EnumerateArray(),
            action => action.GetProperty("name").GetString() ==
                      RecordingInputContract.SteamVrToggleActionPath);
        Assert.Equal("boolean", toggle.GetProperty("type").GetString());
        Assert.Equal("mandatory", toggle.GetProperty("requirement").GetString());

        var localizations = root.GetProperty("localization")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("language_tag").GetString()!,
                StringComparer.Ordinal);
        Assert.Equal(
            "Toggle recording",
            localizations["en_US"]
                .GetProperty(RecordingInputContract.SteamVrToggleActionPath)
                .GetString());
        Assert.Equal(
            "録画を開始または停止",
            localizations["ja_JP"]
                .GetProperty(RecordingInputContract.SteamVrToggleActionPath)
                .GetString());

        var bindings = root.GetProperty("default_bindings")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("controller_type").GetString()!,
                item => item.GetProperty("binding_url").GetString()!,
                StringComparer.Ordinal);
        Assert.Equal(
            ExpectedControllerTypes,
            bindings.Keys.Order(StringComparer.Ordinal));
        foreach (var binding in bindings)
        {
            var bindingPath = Path.Combine(openVrDirectory, binding.Value);
            Assert.True(File.Exists(bindingPath), bindingPath);
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(bindingPath));
            Assert.Equal(
                binding.Key,
                document.RootElement.GetProperty("controller_type").GetString());
            var sources = document.RootElement
                .GetProperty("bindings")
                .GetProperty("/actions/vrrecorder")
                .GetProperty("sources")
                .EnumerateArray()
                .ToArray();
            Assert.Contains(sources, source =>
                source.GetProperty("inputs")
                    .GetProperty("click")
                    .GetProperty("output")
                    .GetString() ==
                RecordingInputContract.SteamVrToggleActionPath);
            Assert.DoesNotContain(sources, source =>
                source.GetProperty("path")
                    .GetString()?
                    .Contains("/input/system", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void EveryControllerBindingIncludesMicrophoneAndRecenterActions()
    {
        var openVrDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VRRecorder.Infrastructure.SteamVr",
            "OpenVr");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(openVrDirectory, "actions.json")));
        var actionNames = manifest.RootElement
            .GetProperty("actions")
            .EnumerateArray()
            .Select(action => action.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains(
            WristOverlayInputContract.SteamVrRecenterActionPath,
            actionNames);

        foreach (var binding in manifest.RootElement
                     .GetProperty("default_bindings")
                     .EnumerateArray())
        {
            var controllerType = binding
                .GetProperty("controller_type")
                .GetString()!;
            var expected = ExpectedPlacementBindings[controllerType];
            var bindingPath = Path.Combine(
                openVrDirectory,
                binding.GetProperty("binding_url").GetString()!);
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(bindingPath));
            var sources = document.RootElement
                .GetProperty("bindings")
                .GetProperty(RecordingInputContract.SteamVrActionSetPath)
                .GetProperty("sources")
                .EnumerateArray()
                .ToArray();
            var outputs = sources
                .Select(source => source.GetProperty("inputs")
                    .GetProperty("click")
                    .GetProperty("output")
                    .GetString())
                .ToArray();
            Assert.Single(outputs, output => string.Equals(
                output,
                RecordingInputContract.SteamVrToggleMicrophoneActionPath,
                StringComparison.Ordinal));
            Assert.Single(outputs, output => string.Equals(
                output,
                WristOverlayInputContract.SteamVrRecenterActionPath,
                StringComparison.Ordinal));
            Assert.Contains(sources, source => SourceMatches(
                source,
                expected.Microphone,
                RecordingInputContract
                    .SteamVrToggleMicrophoneActionPath));
            Assert.Contains(sources, source => SourceMatches(
                source,
                expected.Recenter,
                WristOverlayInputContract.SteamVrRecenterActionPath));
            Assert.Equal(
                sources.Length,
                sources.Select(source => source
                        .GetProperty("path")
                        .GetString())
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.DoesNotContain(sources, source => source
                .GetProperty("path")
                .GetString()?
                .Contains("/input/system", StringComparison.Ordinal) == true);
        }
    }

    private static bool SourceMatches(
        JsonElement source,
        string path,
        string output) =>
        string.Equals(
            source.GetProperty("path").GetString(),
            path,
            StringComparison.Ordinal) &&
        string.Equals(
            source.GetProperty("inputs")
                .GetProperty("click")
                .GetProperty("output")
                .GetString(),
            output,
            StringComparison.Ordinal);

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

    private static ScriptedSteamVrInputRuntime TwoPressesWithRelease() =>
        new(
        [
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: false,
                Changed: true),
            new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true),
        ]);

    private sealed class ScriptedSteamVrInputRuntime : ISteamVrInputRuntime
    {
        private readonly IReadOnlyList<SteamVrDigitalActionState> _states;

        public ScriptedSteamVrInputRuntime(
            IReadOnlyList<SteamVrDigitalActionState> states)
        {
            _states = states;
        }

        public string? RequestedActionPath { get; private set; }

        public async IAsyncEnumerable<SteamVrDigitalActionState>
            ObserveDigitalActionAsync(
                string actionPath,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RequestedActionPath = actionPath;
            foreach (var state in _states)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return state;
                await Task.Yield();
            }
        }
    }

    private sealed class CapturingUiCommandDispatcher : IUiCommandDispatcher
    {
        public List<(UiCommandId Command, UiActivationKind ActivationKind)>
            Commands
        { get; } = [];

        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add((command, activationKind));
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingFirstUiCommandDispatcher
        : IUiCommandDispatcher
    {
        public List<(UiCommandId Command, UiActivationKind ActivationKind)>
            Attempts
        { get; } = [];

        public List<(UiCommandId Command, UiActivationKind ActivationKind)>
            Completed
        { get; } = [];

        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add((command, activationKind));
            if (Attempts.Count == 1)
            {
                throw new InvalidOperationException(
                    "The command is not available in the current state.");
            }

            Completed.Add((command, activationKind));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingPlacementCommands
        : IWristOverlayPlacementCommands
    {
        public int RecenterCount { get; private set; }

        public Task<VrOverlayPlacement> RecenterAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecenterCount++;
            return Task.FromResult(new VrOverlayPlacement(
                OverlayPlacementMode.WristDock,
                WristOverlayPoseContract
                    .CreateDefaultWristDockTransform()));
        }
    }
}
