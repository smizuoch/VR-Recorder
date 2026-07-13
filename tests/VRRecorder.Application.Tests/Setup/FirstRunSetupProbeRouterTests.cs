using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class FirstRunSetupProbeRouterTests
{
    [Fact]
    public async Task RoutesOnlyToProbeRegisteredForExactStep()
    {
        var steamVr = new StubProbe(result: true);
        var osc = new StubProbe(result: false);
        var router = new FirstRunSetupProbeRouter(
            new Dictionary<FirstRunSetupStep, IFirstRunSetupProbe>
            {
                [FirstRunSetupStep.SteamVrDetection] = steamVr,
                [FirstRunSetupStep.VrChatOscDetection] = osc,
            });

        var result = await router.VerifyAsync(
            FirstRunSetupStep.VrChatOscDetection,
            CancellationToken.None);

        Assert.False(result);
        Assert.Empty(steamVr.Steps);
        Assert.Equal([FirstRunSetupStep.VrChatOscDetection], osc.Steps);
    }

    [Fact]
    public async Task UnregisteredStepFailsWithoutInvokingAnotherProbe()
    {
        var steamVr = new StubProbe(result: true);
        var router = new FirstRunSetupProbeRouter(
            new Dictionary<FirstRunSetupStep, IFirstRunSetupProbe>
            {
                [FirstRunSetupStep.SteamVrDetection] = steamVr,
            });

        Assert.False(await router.VerifyAsync(
            FirstRunSetupStep.CameraOscEndpoint,
            CancellationToken.None));
        Assert.Empty(steamVr.Steps);
    }

    private sealed class StubProbe(bool result) : IFirstRunSetupProbe
    {
        public List<FirstRunSetupStep> Steps { get; } = [];

        public Task<bool> VerifyAsync(
            FirstRunSetupStep setupStep,
            CancellationToken cancellationToken)
        {
            Steps.Add(setupStep);
            return Task.FromResult(result);
        }
    }
}
