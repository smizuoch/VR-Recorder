using System.Runtime.CompilerServices;
using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Infrastructure.SteamVr;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.IntegrationTests.Input;

public sealed class RecordingInputParityIntegrationTests
{
    [Fact]
    public async Task DesktopKeyboardTrayWristAndSteamVrUseOneRecordingCommand()
    {
        var commands = new CapturingUiCommandDispatcher();
        var recordingInputs = new RecordingInputDispatcher(commands);

        await recordingInputs.DispatchAsync(
            UiActivationKind.DesktopClick,
            CancellationToken.None);
        await recordingInputs.DispatchAsync(
            UiActivationKind.DesktopKeyboard,
            CancellationToken.None);
        await recordingInputs.DispatchAsync(
            UiActivationKind.DesktopTray,
            CancellationToken.None);
        var wristAction = Assert.Single(new WristUiProjector(
                EnglishUiLocalizer.Instance)
            .Project(new RecorderStatusSnapshot(
                Revision: 1,
                State: RecorderState.Ready,
                AvailableActions: RecorderAvailableActions.Start))
            .Actions);
        await new WristInputAdapter(commands).ActivateAsync(
            wristAction,
            CancellationToken.None);
        await new SteamVrRecordingInputAdapter(
                new OnePressSteamVrInputRuntime(),
                recordingInputs)
            .RunAsync(CancellationToken.None);

        Assert.Equal(
            Enum.GetValues<UiActivationKind>(),
            commands.Commands.Select(command => command.ActivationKind));
        Assert.All(commands.Commands, command =>
            Assert.Equal(UiCommandId.ToggleRecording, command.Command));
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

    private sealed class OnePressSteamVrInputRuntime : ISteamVrInputRuntime
    {
        public async IAsyncEnumerable<SteamVrDigitalActionState>
            ObserveDigitalActionAsync(
                string actionPath,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.Equal(
                RecordingInputContract.SteamVrToggleActionPath,
                actionPath);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SteamVrDigitalActionState(
                IsActive: true,
                State: true,
                Changed: true);
            await Task.CompletedTask;
        }
    }
}
