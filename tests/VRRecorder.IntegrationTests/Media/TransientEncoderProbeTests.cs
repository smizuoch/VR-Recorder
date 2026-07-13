using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Encoding;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class TransientEncoderProbeTests
{
    [Fact]
    public async Task CreatesProbeOnDemandAndDisposesItAfterResult()
    {
        StubProbe? created = null;
        var transient = new TransientEncoderProbe(() =>
        {
            created = new StubProbe();
            return created;
        });
        var request = new EncoderProbeRequest(
            EncoderKind.MediaFoundationSoftware,
            adapterLuid: 0,
            "setup-self-test");

        var result = await transient.ProbeAsync(
            request,
            CancellationToken.None);

        Assert.Equal(EncoderProbeResult.PacketProduced, result);
        Assert.NotNull(created);
        Assert.Same(request, created.Request);
        Assert.True(created.Disposed);
    }

    private sealed class StubProbe : IEncoderProbe, IAsyncDisposable
    {
        public EncoderProbeRequest? Request { get; private set; }

        public bool Disposed { get; private set; }

        public Task<EncoderProbeResult> ProbeAsync(
            EncoderProbeRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(EncoderProbeResult.PacketProduced);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
