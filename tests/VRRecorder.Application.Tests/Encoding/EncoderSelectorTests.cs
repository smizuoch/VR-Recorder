using VRRecorder.Application.Encoding;
using VRRecorder.Application.Tests.TestDoubles;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Tests.Encoding;

public sealed class EncoderSelectorTests
{
    [Fact]
    public async Task FailedNvencProbeFallsBackToMediaFoundationSoftware()
    {
        var probe = new ScriptedEncoderProbe(
            (EncoderKind.Nvenc, EncoderProbeResult.Failed),
            (EncoderKind.MediaFoundationSoftware, EncoderProbeResult.PacketProduced));
        var selector = new EncoderSelector(probe);

        var selected = await selector.SelectAsync(
            EncoderPreference.Auto,
            GpuVendor.Nvidia,
            CancellationToken.None);

        Assert.Equal(EncoderKind.MediaFoundationSoftware, selected);
        Assert.Equal(
            [EncoderKind.Nvenc, EncoderKind.MediaFoundationSoftware],
            probe.ProbedEncoders);
    }
}
