using System.Globalization;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;
using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class CameraOscEndpointFirstRunSetupProbeTests
{
    [Fact]
    public async Task OtherStepDoesNotDiscoverOrCreateGateway()
    {
        var discovery = new CountingDiscovery();
        var factory = new StubFactory(new PlainGateway());
        var probe = new CameraOscEndpointFirstRunSetupProbe(
            discovery,
            factory);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.WristOverlayPlacement,
            CancellationToken.None));
        Assert.Equal(0, discovery.CallCount);
        Assert.Null(factory.Candidate);
    }

    [Fact]
    public async Task NullDiscoveryResultIsRejected()
    {
        var probe = new CameraOscEndpointFirstRunSetupProbe(
            new NullDiscovery(),
            new StubFactory(new PlainGateway()));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            probe.VerifyAsync(
                FirstRunSetupStep.CameraOscEndpoint,
                CancellationToken.None));
    }

    [Fact]
    public async Task SynchronousGatewayIsDisposedAfterVerification()
    {
        var gateway = new SyncDisposableGateway();
        var probe = new CameraOscEndpointFirstRunSetupProbe(
            new StubDiscovery([Candidate("sync-dispose")]),
            new StubFactory(gateway));

        Assert.True(await probe.VerifyAsync(
            FirstRunSetupStep.CameraOscEndpoint,
            CancellationToken.None));
        Assert.True(gateway.Disposed);
    }

    [Fact]
    public async Task GatewayWithoutDisposalContractCanStillVerify()
    {
        var gateway = new PlainGateway();
        var probe = new CameraOscEndpointFirstRunSetupProbe(
            new StubDiscovery([Candidate("plain")]),
            new StubFactory(gateway));

        Assert.True(await probe.VerifyAsync(
            FirstRunSetupStep.CameraOscEndpoint,
            CancellationToken.None));
        Assert.Equal([CameraMode.Photo], gateway.WrittenModes);
    }

    [Fact]
    public async Task SingleCandidateReadsAndWritesBackSameModeWithConfirmation()
    {
        var candidate = Candidate("only");
        var gateway = new StubGateway(new CameraSnapshot(
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false)));
        var factory = new StubFactory(gateway);
        var probe = new CameraOscEndpointFirstRunSetupProbe(
            new StubDiscovery([candidate]),
            factory);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.CameraOscEndpoint,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Same(candidate, factory.Candidate);
        Assert.Equal([CameraMode.Photo], gateway.WrittenModes);
        Assert.True(gateway.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MissingOrAmbiguousCandidateDoesNotWrite(int count)
    {
        var gateway = new StubGateway(new CameraSnapshot(
            ObservedCameraValue.Known(CameraMode.Stream),
            ObservedCameraValue.Known(true)));
        var factory = new StubFactory(gateway);
        var candidates = Enumerable.Range(0, count)
            .Select(index => Candidate(index.ToString(
                CultureInfo.InvariantCulture)))
            .ToArray();
        var probe = new CameraOscEndpointFirstRunSetupProbe(
            new StubDiscovery(candidates),
            factory);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.CameraOscEndpoint,
            CancellationToken.None));
        Assert.Null(factory.Candidate);
        Assert.Empty(gateway.WrittenModes);
    }

    [Fact]
    public async Task UnknownCurrentModeDoesNotRiskAStateChangingWrite()
    {
        var gateway = new StubGateway(new CameraSnapshot(
            ObservedCameraValue.Unknown<CameraMode>(),
            ObservedCameraValue.Known(false)));
        var probe = new CameraOscEndpointFirstRunSetupProbe(
            new StubDiscovery([Candidate("only")]),
            new StubFactory(gateway));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.CameraOscEndpoint,
            CancellationToken.None));
        Assert.Empty(gateway.WrittenModes);
        Assert.True(gateway.Disposed);
    }

    private static VrChatInstanceCandidate Candidate(string suffix) => new(
        $"service-{suffix}",
        $"VRChat-Client-{suffix}",
        new Uri("http://127.0.0.1:9001/"),
        "127.0.0.1",
        9000);

    private sealed class StubDiscovery(
        IReadOnlyList<VrChatInstanceCandidate> candidates)
        : IVrChatInstanceDiscovery
    {
        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken) => Task.FromResult(candidates);
    }

    private sealed class CountingDiscovery : IVrChatInstanceDiscovery
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<VrChatInstanceCandidate>>([]);
        }
    }

    private sealed class NullDiscovery : IVrChatInstanceDiscovery
    {
        public Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VrChatInstanceCandidate>>(null!);
    }

    private sealed class StubFactory(IVrChatCameraGateway gateway)
        : IVrChatCameraGatewayFactory
    {
        public VrChatInstanceCandidate? Candidate { get; private set; }

        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate)
        {
            Candidate = candidate;
            return gateway;
        }
    }

    private sealed class StubGateway(CameraSnapshot snapshot)
        : IVrChatCameraGateway, IAsyncDisposable
    {
        public List<CameraMode> WrittenModes { get; } = [];

        public bool Disposed { get; private set; }

        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken) => Task.FromResult(snapshot);

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            WrittenModes.Add(mode);
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private class PlainGateway : IVrChatCameraGateway
    {
        public List<CameraMode> WrittenModes { get; } = [];

        public Task<CameraSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new CameraSnapshot(
                ObservedCameraValue.Known(CameraMode.Photo),
                ObservedCameraValue.Known(false)));

        public Task SetModeAsync(
            CameraMode mode,
            CancellationToken cancellationToken)
        {
            WrittenModes.Add(mode);
            return Task.CompletedTask;
        }

        public Task SetStreamingAsync(
            bool enabled,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SyncDisposableGateway : PlainGateway, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
