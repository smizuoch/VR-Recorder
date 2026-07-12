using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class FirstRunSetupVerificationControllerTests
{
    [Fact]
    public async Task SuccessfulProbeCompletesExactlyTheCurrentStep()
    {
        var store = new StubStore(progress: null);
        var probe = new StubProbe(succeeded: true);
        var controller = new FirstRunSetupVerificationController(
            new FirstRunSetupController(store),
            probe);

        var result = await controller.VerifyCurrentAsync(
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(FirstRunSetupStep.SteamVrDetection, result.VerifiedStep);
        Assert.Equal(
            FirstRunSetupStep.VrChatOscDetection,
            result.Setup.CurrentStep);
        Assert.Equal(
            [FirstRunSetupStep.SteamVrDetection],
            store.Progress!.CompletedSteps);
        Assert.Equal(
            [FirstRunSetupStep.SteamVrDetection],
            probe.RequestedSteps);
    }

    [Fact]
    public async Task FailedProbeDoesNotPersistOrAdvance()
    {
        var store = new StubStore(progress: null);
        var controller = new FirstRunSetupVerificationController(
            new FirstRunSetupController(store),
            new StubProbe(succeeded: false));

        var result = await controller.VerifyCurrentAsync(
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(FirstRunSetupStep.SteamVrDetection, result.VerifiedStep);
        Assert.Equal(FirstRunSetupStep.SteamVrDetection, result.Setup.CurrentStep);
        Assert.Equal(0, store.SaveCount);
        Assert.Null(store.Progress);
    }

    [Fact]
    public async Task CompletedSetupDoesNotInvokeProbe()
    {
        var store = new StubStore(new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            FirstRunSetupController.RequiredSteps));
        var probe = new StubProbe(succeeded: true);
        var controller = new FirstRunSetupVerificationController(
            new FirstRunSetupController(store),
            probe);

        var result = await controller.VerifyCurrentAsync(
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.VerifiedStep);
        Assert.True(result.Setup.IsComplete);
        Assert.Empty(probe.RequestedSteps);
        Assert.Equal(0, store.SaveCount);
    }

    private sealed class StubProbe(bool succeeded) : IFirstRunSetupProbe
    {
        public List<FirstRunSetupStep> RequestedSteps { get; } = [];

        public Task<bool> VerifyAsync(
            FirstRunSetupStep setupStep,
            CancellationToken cancellationToken)
        {
            RequestedSteps.Add(setupStep);
            return Task.FromResult(succeeded);
        }
    }

    private sealed class StubStore(FirstRunSetupProgress? progress)
        : IFirstRunSetupStore
    {
        public FirstRunSetupProgress? Progress { get; private set; } = progress;

        public int SaveCount { get; private set; }

        public Task<FirstRunSetupProgress?> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(Progress);

        public Task SaveAsync(
            FirstRunSetupProgress updated,
            CancellationToken cancellationToken)
        {
            Progress = updated;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
