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

    [Theory]
    [InlineData(GpuVendor.Amd, EncoderKind.Amf)]
    [InlineData(GpuVendor.Intel, EncoderKind.Qsv)]
    public async Task AutoProbesMatchingVendorEncoderFirst(
        GpuVendor vendor,
        EncoderKind expected)
    {
        var probe = new ScriptedEncoderProbe(
            (expected, EncoderProbeResult.PacketProduced));
        var selector = new EncoderSelector(probe);

        var selected = await selector.SelectAsync(
            EncoderPreference.Auto,
            vendor,
            CancellationToken.None);

        Assert.Equal(expected, selected);
        Assert.Equal([expected], probe.ProbedEncoders);
    }

    [Fact]
    public async Task FailedFixedPreferenceDoesNotSilentlyFallBack()
    {
        var probe = new ScriptedEncoderProbe(
            (EncoderKind.Nvenc, EncoderProbeResult.Failed));
        var selector = new EncoderSelector(probe);

        var exception = await Assert.ThrowsAsync<EncoderUnavailableException>(() =>
            selector.SelectAsync(
                EncoderPreference.Nvenc,
                GpuVendor.Nvidia,
                CancellationToken.None));

        Assert.Equal(EncoderPreference.Nvenc, exception.Preference);
        Assert.Equal([EncoderKind.Nvenc], probe.ProbedEncoders);
    }
}
