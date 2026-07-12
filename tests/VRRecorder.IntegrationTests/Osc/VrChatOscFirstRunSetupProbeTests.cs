using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class VrChatOscFirstRunSetupProbeTests
{
    [Fact]
    public async Task ValidDiscoveredVrChatInstanceVerifiesOscDetection()
    {
        var candidate = new VrChatInstanceCandidate(
            "vrchat-service",
            "VRChat-Client-1",
            new Uri("http://127.0.0.1:9001/"),
            "127.0.0.1",
            9000);
        var discovery = new StubDiscovery([candidate]);
        var probe = new VrChatOscFirstRunSetupProbe(discovery);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.VrChatOscDetection,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal(1, discovery.CallCount);
    }

    [Fact]
    public async Task NoValidInstanceLeavesOscDetectionIncomplete()
    {
        var probe = new VrChatOscFirstRunSetupProbe(new StubDiscovery([]));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.VrChatOscDetection,
            CancellationToken.None));
    }

    [Fact]
    public async Task ProbeDoesNotRunDiscoveryForAnotherSetupStep()
    {
        var discovery = new StubDiscovery([]);
        var probe = new VrChatOscFirstRunSetupProbe(discovery);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.CameraOscEndpoint,
            CancellationToken.None));
        Assert.Equal(0, discovery.CallCount);
    }

    private sealed class StubDiscovery(
        IReadOnlyList<VrChatInstanceCandidate> candidates)
        : IVrChatInstanceDiscovery
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(candidates);
        }
    }
}
