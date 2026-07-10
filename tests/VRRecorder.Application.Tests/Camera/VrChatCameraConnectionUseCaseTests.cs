using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Application.Tests.Camera;

public sealed class VrChatCameraConnectionUseCaseTests
{
    [Fact]
    public async Task MultipleInstancesCreateNoGatewayUntilExactSelection()
    {
        var first = Candidate("service-a", 9000);
        var second = Candidate("service-b", 9010);
        var gateways = new CapturingVrChatCameraGatewayFactory();
        var useCase = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new StubDiscovery([second, first])),
            gateways);

        var unresolved = await useCase.ResolveAsync(
            selectedServiceId: null,
            CancellationToken.None);

        var selection = Assert.IsType<
            VrChatCameraConnectionResolution.SelectionRequired>(unresolved);
        Assert.Equal(new[] { first, second }, selection.Candidates);
        Assert.Empty(gateways.CreatedFor);

        var resolved = await useCase.ResolveAsync(
            selectedServiceId: second.ServiceId,
            CancellationToken.None);

        var connected = Assert.IsType<
            VrChatCameraConnectionResolution.Connected>(resolved);
        Assert.Equal(second, connected.Candidate);
        Assert.Same(gateways.Gateway, connected.Gateway);
        Assert.Equal(new[] { second }, gateways.CreatedFor);
        Assert.Equal(0, gateways.Gateway.WriteCount);
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

    private sealed class CapturingVrChatCameraGatewayFactory
        : IVrChatCameraGatewayFactory
    {
        public CapturingVrChatCameraGateway Gateway { get; } = new();

        public List<VrChatInstanceCandidate> CreatedFor { get; } = [];

        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate)
        {
            CreatedFor.Add(candidate);
            return Gateway;
        }
    }

    private sealed class CapturingVrChatCameraGateway : IVrChatCameraGateway
    {
        public int WriteCount { get; private set; }

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.CompletedTask;
        }
    }
}
