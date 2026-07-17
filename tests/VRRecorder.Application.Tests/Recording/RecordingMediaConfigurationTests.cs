using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Recording;

public sealed class RecordingMediaConfigurationTests
{
    [Fact]
    public void DefaultsAndVideoSourceBindingPreserveExplicitMediaChoices()
    {
        var defaults = RecordingMediaConfiguration.CreateDefault();
        var legacy = defaults.WithVideoSource(new StableVideoSignal(640, 480));
        var discovered = defaults.WithVideoSource(new StableVideoSignal(
            "spout-sender",
            42,
            "NVIDIA_GPU",
            GpuVendor.Nvidia,
            1920,
            1080,
            VideoPixelFormat.Bgra8,
            60));
        var muted = discovered.WithAudioRouting(AudioRouting.Muted);

        Assert.Same(defaults, legacy);
        Assert.Equal("spout-sender", discovered.SpoutSenderIdentity);
        Assert.Equal(42UL, discovered.SpoutAdapterLuid);
        Assert.Equal(42UL, discovered.EncoderAdapterLuid);
        Assert.Equal("NVIDIA_GPU", discovered.GpuIdentity);
        Assert.Equal(AudioRouting.Muted, muted.AudioRouting);
        Assert.Equal(discovered.DesktopEndpointId, muted.DesktopEndpointId);
        Assert.Equal(discovered.MicrophoneEndpointId, muted.MicrophoneEndpointId);
        Assert.Equal(discovered.QualityPreset, muted.QualityPreset);
        Assert.Throws<ArgumentNullException>(() => defaults.WithVideoSource(null!));
    }

    [Fact]
    public void RejectsUnknownEnumsMalformedIdentityAndGainOutsideContract()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(audioRouting: (AudioRouting)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(qualityPreset: (VideoQualityPreset)int.MaxValue));
        Assert.Throws<ArgumentException>(() =>
            Create(desktopEndpointId: " "));
        Assert.Throws<ArgumentException>(() =>
            Create(microphoneEndpointId: "capture\nendpoint"));
        Assert.Throws<ArgumentException>(() =>
            Create(spoutSenderIdentity: new string('s', 4_097)));
        Assert.Throws<ArgumentException>(() =>
            Create(gpuIdentity: "gpu\0identity"));
        Assert.Throws<ArgumentException>(() =>
            Create(gpuIdentity: "\ud800"));

        foreach (var gain in new[]
                 {
                     double.NaN,
                     double.PositiveInfinity,
                     -96.001,
                     24.001,
                 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Create(desktopGainDb: gain));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Create(microphoneGainDb: gain));
        }

        _ = Create(
            desktopGainDb: RecordingMediaConfiguration.MinimumInputGainDb,
            microphoneGainDb: RecordingMediaConfiguration.MaximumInputGainDb);
    }

    private static RecordingMediaConfiguration Create(
        AudioRouting audioRouting = AudioRouting.Mixed,
        string desktopEndpointId = "default-render",
        string microphoneEndpointId = "default-capture",
        double desktopGainDb = -6,
        double microphoneGainDb = -6,
        VideoQualityPreset qualityPreset = VideoQualityPreset.High,
        string spoutSenderIdentity = "spout-sender",
        string gpuIdentity = "GPU_1234") =>
        new(
            audioRouting,
            desktopEndpointId,
            microphoneEndpointId,
            desktopGainDb,
            microphoneGainDb,
            qualityPreset,
            spoutSenderIdentity,
            42,
            42,
            gpuIdentity);
}
