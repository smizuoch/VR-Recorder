using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.Camera;

public sealed class VrChatTargetResolverTests
{
    [Fact]
    public async Task SingleInstanceIsSelectedWithoutPrompt()
    {
        var candidate = Candidate("service-a", 9001);
        var resolver = new VrChatTargetResolver(
            new StubDiscovery([candidate]));

        var result = await resolver.ResolveAsync(
            selectedServiceId: null,
            CancellationToken.None);

        Assert.Equal(
            candidate,
            Assert.IsType<VrChatTargetResolution.Selected>(result).Candidate);
    }

    [Fact]
    public async Task MultipleInstancesRequireExplicitMatchingSelection()
    {
        var first = Candidate("service-a", 9001);
        var second = Candidate("service-b", 9002);
        var resolver = new VrChatTargetResolver(
            new StubDiscovery([second, first]));

        var unresolved = await resolver.ResolveAsync(
            selectedServiceId: null,
            CancellationToken.None);
        var selection = Assert.IsType<
            VrChatTargetResolution.SelectionRequired>(unresolved);

        Assert.Equal(new[] { first, second }, selection.Candidates);

        var resolved = await resolver.ResolveAsync(
            selectedServiceId: second.ServiceId,
            CancellationToken.None);
        Assert.Equal(
            second,
            Assert.IsType<VrChatTargetResolution.Selected>(resolved).Candidate);
    }

    [Fact]
    public async Task StaleSelectionDoesNotSilentlyChooseAnotherInstance()
    {
        var candidate = Candidate("service-new", 9001);
        var resolver = new VrChatTargetResolver(
            new StubDiscovery([candidate]));

        var result = await resolver.ResolveAsync(
            selectedServiceId: "service-stale",
            CancellationToken.None);

        var selection = Assert.IsType<
            VrChatTargetResolution.SelectionRequired>(result);
        Assert.Equal(new[] { candidate }, selection.Candidates);
    }

    [Fact]
    public async Task DuplicateServiceIdentityFailsClosed()
    {
        var resolver = new VrChatTargetResolver(new StubDiscovery(
        [
            Candidate("duplicate", 9001),
            Candidate("duplicate", 9002),
        ]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task NoInstancesReturnsNotFound()
    {
        var resolver = new VrChatTargetResolver(new StubDiscovery([]));

        var result = await resolver.ResolveAsync(
            selectedServiceId: null,
            CancellationToken.None);

        Assert.IsType<VrChatTargetResolution.NotFound>(result);
    }

    private static VrChatInstanceCandidate Candidate(
        string serviceId,
        int oscPort) =>
        new(
            serviceId,
            $"VRChat {serviceId}",
            new Uri($"http://127.0.0.1:{oscPort + 1000}/"),
            "127.0.0.1",
            oscPort);

    private sealed class StubDiscovery : IVrChatInstanceDiscovery
    {
        private readonly IReadOnlyList<VrChatInstanceCandidate> _candidates;

        public StubDiscovery(IReadOnlyList<VrChatInstanceCandidate> candidates)
        {
            _candidates = candidates;
        }

        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_candidates);
        }
    }
}
