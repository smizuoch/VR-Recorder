using System.Runtime.CompilerServices;
using VRRecorder.Application.Setup;
using VRRecorder.DesignSystem;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class SteamVrActionBindingFirstRunSetupProbeTests
{
    [Fact]
    public void RejectsNonPositiveOrInfiniteTimeout()
    {
        foreach (var timeout in new[]
                 {
                     TimeSpan.Zero,
                     TimeSpan.FromMilliseconds(-1),
                     Timeout.InfiniteTimeSpan,
                 })
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SteamVrActionBindingFirstRunSetupProbe(
                    () => new StubRuntime(isActive: true),
                    timeout));
            Assert.Equal("timeout", exception.ParamName);
        }
    }

    [Fact]
    public async Task NullRuntimeFactoryResultIsRejected()
    {
        var probe = new SteamVrActionBindingFirstRunSetupProbe(
            () => null!);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            probe.VerifyAsync(
                FirstRunSetupStep.SteamVrActionBinding,
                CancellationToken.None));
    }

    [Fact]
    public async Task EmptyActionStreamLeavesBindingIncomplete()
    {
        var probe = new SteamVrActionBindingFirstRunSetupProbe(
            () => new EmptyRuntime());

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.SteamVrActionBinding,
            CancellationToken.None));
    }

    [Fact]
    public async Task ProbeTimeoutLeavesBindingIncomplete()
    {
        var probe = new SteamVrActionBindingFirstRunSetupProbe(
            () => new BlockingRuntime(),
            TimeSpan.FromMilliseconds(20));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.SteamVrActionBinding,
            CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsIncompleteBinding()
    {
        var probe = new SteamVrActionBindingFirstRunSetupProbe(
            () => new BlockingRuntime(),
            TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            probe.VerifyAsync(
                FirstRunSetupStep.SteamVrActionBinding,
                cancellation.Token));
    }

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

    private sealed class EmptyRuntime : ISteamVrInputRuntime
    {
        public async IAsyncEnumerable<SteamVrDigitalActionState>
            ObserveDigitalActionAsync(
                string actionPath,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class BlockingRuntime : ISteamVrInputRuntime
    {
        public async IAsyncEnumerable<SteamVrDigitalActionState>
            ObserveDigitalActionAsync(
                string actionPath,
                [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }
}
