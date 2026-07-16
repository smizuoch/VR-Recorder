using System.Runtime.CompilerServices;
using VRRecorder.Application.Setup;
using VRRecorder.DesignSystem;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class SteamVrActionBindingFirstRunSetupProbeTests
{
    [Fact]
    public async Task ActiveRecordingAndMicrophoneActionsVerifyBindings()
    {
        var runtime = new StubRuntime(isActive: true);
        var probe = new SteamVrActionBindingFirstRunSetupProbe(() => runtime);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.SteamVrActionBinding,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal(
            [
                RecordingInputContract.SteamVrToggleActionPath,
                RecordingInputContract.SteamVrToggleMicrophoneActionPath,
                WristOverlayInputContract.SteamVrRecenterActionPath,
            ],
            runtime.ActionPaths);
    }

    [Fact]
    public async Task InactiveActionLeavesBindingIncomplete()
    {
        var runtime = new StubRuntime(isActive: false);
        var probe = new SteamVrActionBindingFirstRunSetupProbe(() => runtime);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.SteamVrActionBinding,
            CancellationToken.None));
    }

    [Fact]
    public async Task OtherStepDoesNotCreateRuntime()
    {
        var createCount = 0;
        var probe = new SteamVrActionBindingFirstRunSetupProbe(() =>
        {
            createCount++;
            return new StubRuntime(isActive: true);
        });

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.WristOverlayPlacement,
            CancellationToken.None));
        Assert.Equal(0, createCount);
    }

    private sealed class StubRuntime(bool isActive) : ISteamVrInputRuntime
    {
        public List<string> ActionPaths { get; } = [];

        public async IAsyncEnumerable<SteamVrDigitalActionState>
            ObserveDigitalActionAsync(
                string actionPath,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ActionPaths.Add(actionPath);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SteamVrDigitalActionState(
                isActive,
                State: false,
                Changed: false);
            await Task.CompletedTask;
        }
    }
}
