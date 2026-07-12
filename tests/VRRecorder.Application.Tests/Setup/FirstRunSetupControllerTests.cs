using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class FirstRunSetupControllerTests
{
    [Fact]
    public async Task LoadResumesAtFirstIncompleteStep()
    {
        var store = new StubFirstRunSetupStore(new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            [
                FirstRunSetupStep.SteamVrDetection,
                FirstRunSetupStep.VrChatOscDetection,
            ]));
        var controller = new FirstRunSetupController(store);

        var snapshot = await controller.LoadAsync(CancellationToken.None);

        Assert.False(snapshot.IsComplete);
        Assert.Equal(
            FirstRunSetupStep.CameraOscEndpoint,
            snapshot.CurrentStep);
        Assert.Equal(2, snapshot.CompletedStepCount);
    }

    [Fact]
    public async Task LoadOfOldSetupVersionStartsAtFirstStepWithoutClaimingCompletion()
    {
        var store = new StubFirstRunSetupStore(new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion - 1,
            Enum.GetValues<FirstRunSetupStep>()));
        var controller = new FirstRunSetupController(store);

        var snapshot = await controller.LoadAsync(CancellationToken.None);

        Assert.Equal(FirstRunSetupStep.SteamVrDetection, snapshot.CurrentStep);
        Assert.Equal(0, snapshot.CompletedStepCount);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CompleteCurrentStepPersistsAndAdvances()
    {
        var store = new StubFirstRunSetupStore(progress: null);
        var controller = new FirstRunSetupController(store);

        var snapshot = await controller.CompleteAsync(
            FirstRunSetupStep.SteamVrDetection,
            CancellationToken.None);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(
            FirstRunSetupController.CurrentSetupVersion,
            store.Progress!.SetupVersion);
        Assert.Equal(
            [FirstRunSetupStep.SteamVrDetection],
            store.Progress.CompletedSteps);
        Assert.Equal(
            FirstRunSetupStep.VrChatOscDetection,
            snapshot.CurrentStep);
    }

    [Fact]
    public async Task CompleteRejectsOutOfOrderStepWithoutSaving()
    {
        var store = new StubFirstRunSetupStore(progress: null);
        var controller = new FirstRunSetupController(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.CompleteAsync(
                FirstRunSetupStep.EncoderSelfTest,
                CancellationToken.None));

        Assert.Equal(0, store.SaveCount);
        Assert.Null(store.Progress);
    }

    [Fact]
    public async Task CompletingEveryRequiredStepProducesCompleteSnapshot()
    {
        var store = new StubFirstRunSetupStore(progress: null);
        var controller = new FirstRunSetupController(store);
        FirstRunSetupSnapshot? snapshot = null;

        foreach (var step in FirstRunSetupController.RequiredSteps)
        {
            snapshot = await controller.CompleteAsync(
                step,
                CancellationToken.None);
        }

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsComplete);
        Assert.Null(snapshot.CurrentStep);
        Assert.Equal(12, snapshot.CompletedStepCount);
        Assert.Equal(12, store.SaveCount);
    }

    private sealed class StubFirstRunSetupStore : IFirstRunSetupStore
    {
        public StubFirstRunSetupStore(FirstRunSetupProgress? progress)
        {
            Progress = progress;
        }

        public FirstRunSetupProgress? Progress { get; private set; }

        public int SaveCount { get; private set; }

        public Task<FirstRunSetupProgress?> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(Progress);

        public Task SaveAsync(
            FirstRunSetupProgress progress,
            CancellationToken cancellationToken)
        {
            Progress = progress;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
