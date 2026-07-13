using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Setup;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Tests.Setup;

public sealed class EncoderSelfTestFirstRunSetupProbeTests
{
    [Theory]
    [InlineData(EncoderPreference.Nvenc, EncoderKind.Nvenc)]
    [InlineData(EncoderPreference.Amf, EncoderKind.Amf)]
    [InlineData(EncoderPreference.Qsv, EncoderKind.Qsv)]
    [InlineData(
        EncoderPreference.MediaFoundationSoftware,
        EncoderKind.MediaFoundationSoftware)]
    [InlineData(EncoderPreference.Auto, EncoderKind.MediaFoundationSoftware)]
    public async Task PacketProducingConfiguredEncoderVerifiesSelfTest(
        EncoderPreference preference,
        EncoderKind expected)
    {
        var nativeProbe = new StubEncoderProbe(EncoderProbeResult.PacketProduced);
        var probe = new EncoderSelfTestFirstRunSetupProbe(
            new StubSettingsStore(SettingsWithEncoder(preference)),
            nativeProbe);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.EncoderSelfTest,
            CancellationToken.None);

        Assert.True(verified);
        var request = Assert.Single(nativeProbe.Requests);
        Assert.Equal(expected, request.Encoder);
        Assert.Equal(1920, request.Width);
        Assert.Equal(1080, request.Height);
        Assert.Equal(30, request.FrameRate.Value);
    }

    [Fact]
    public async Task NoPacketLeavesSelfTestIncomplete()
    {
        var probe = new EncoderSelfTestFirstRunSetupProbe(
            new StubSettingsStore(SettingsWithEncoder(EncoderPreference.Nvenc)),
            new StubEncoderProbe(EncoderProbeResult.Failed));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.EncoderSelfTest,
            CancellationToken.None));
    }

    [Fact]
    public async Task OtherStepDoesNotLoadSettingsOrRunEncoder()
    {
        var settings = new StubSettingsStore(
            SettingsWithEncoder(EncoderPreference.Nvenc));
        var nativeProbe = new StubEncoderProbe(EncoderProbeResult.PacketProduced);
        var probe = new EncoderSelfTestFirstRunSetupProbe(settings, nativeProbe);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.SteamVrActionBinding,
            CancellationToken.None));
        Assert.Equal(0, settings.LoadCount);
        Assert.Empty(nativeProbe.Requests);
    }

    private static VRRecorderSettings SettingsWithEncoder(
        EncoderPreference preference)
    {
        var settings = VRRecorderSettings.CreateDefault();
        return settings with
        {
            Video = settings.Video with { Encoder = preference },
        };
    }

    private sealed class StubEncoderProbe(EncoderProbeResult result)
        : IEncoderProbe
    {
        public List<EncoderProbeRequest> Requests { get; } = [];

        public Task<EncoderProbeResult> ProbeAsync(
            EncoderProbeRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class StubSettingsStore(VRRecorderSettings settings)
        : ISettingsStore
    {
        public int LoadCount { get; private set; }

        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(settings);
        }

        public Task SaveAsync(
            VRRecorderSettings updated,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
