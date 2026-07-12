using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class FirstRunSetupUiControllerTests
{
    [Fact]
    public async Task LoadProjectsCurrentStepAndOverallProgressForTheWindow()
    {
        var progress = new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            [
                FirstRunSetupStep.SteamVrDetection,
                FirstRunSetupStep.VrChatOscDetection,
            ]);
        var controller = new FirstRunSetupUiController(
            new FirstRunSetupController(new StubStore(progress)));

        var view = await controller.LoadAsync(CancellationToken.None);

        Assert.True(view.RequiresSetup);
        Assert.Equal(FirstRunSetupStep.CameraOscEndpoint, view.CurrentStep);
        Assert.Equal("Setup_Step_CameraOscEndpoint_Title", view.TitleResourceKey);
        Assert.Equal("Setup_Step_CameraOscEndpoint_Body", view.BodyResourceKey);
        Assert.Equal(3, view.StepNumber);
        Assert.Equal(12, view.TotalSteps);
        Assert.Equal(2d / 12d * 100d, view.ProgressPercent, precision: 8);
    }

    [Fact]
    public async Task EveryRequiredStepHasStableDistinctResourceKeys()
    {
        var titleKeys = new HashSet<string>(StringComparer.Ordinal);
        var bodyKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var completedCount = 0;
             completedCount < FirstRunSetupController.RequiredSteps.Count;
             completedCount++)
        {
            var progress = new FirstRunSetupProgress(
                FirstRunSetupController.CurrentSetupVersion,
                FirstRunSetupController.RequiredSteps.Take(completedCount));
            var controller = new FirstRunSetupUiController(
                new FirstRunSetupController(new StubStore(progress)));

            var view = await controller.LoadAsync(CancellationToken.None);

            Assert.True(titleKeys.Add(view.TitleResourceKey));
            Assert.True(bodyKeys.Add(view.BodyResourceKey));
            Assert.Equal(completedCount + 1, view.StepNumber);
        }
    }

    [Fact]
    public async Task CompletedSetupDoesNotExposeAPlaceholderStep()
    {
        var progress = new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            FirstRunSetupController.RequiredSteps);
        var controller = new FirstRunSetupUiController(
            new FirstRunSetupController(new StubStore(progress)));

        var view = await controller.LoadAsync(CancellationToken.None);

        Assert.False(view.RequiresSetup);
        Assert.Null(view.CurrentStep);
        Assert.Equal(string.Empty, view.TitleResourceKey);
        Assert.Equal(string.Empty, view.BodyResourceKey);
        Assert.Equal(12, view.StepNumber);
        Assert.Equal(100, view.ProgressPercent);
    }

    private sealed class StubStore(FirstRunSetupProgress progress)
        : IFirstRunSetupStore
    {
        public Task<FirstRunSetupProgress?> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                (FirstRunSetupProgress?)progress);

        public Task SaveAsync(
            FirstRunSetupProgress updated,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
