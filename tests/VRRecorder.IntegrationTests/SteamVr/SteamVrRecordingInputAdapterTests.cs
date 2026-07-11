using System.Runtime.CompilerServices;
using System.Text.Json;
using VRRecorder.DesignSystem;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class SteamVrRecordingInputAdapterTests
{
    private static readonly string[] ExpectedControllerTypes =
        ["knuckles", "oculus_touch", "vive_controller"];

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
}
