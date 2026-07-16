using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Tests.Settings;

public sealed class VRRecorderSettingsContractTests
{
    [Fact]
    public void DesignDefaultsUseCurrentSchemaAndEnabledHapticTokens()
    {
        var settings = VRRecorderSettings.CreateDefault();

        Assert.Equal(3, settings.SchemaVersion);
        Assert.True(settings.Vr.HapticsEnabled);
        Assert.Equal(120f, settings.Vr.HapticFrequencyHertz);
        Assert.Equal(0.65f, settings.Vr.HapticAmplitude);
        VRRecorderSettingsContract.Validate(settings);
    }

    [Theory]
    [InlineData(float.NaN, 0.65f)]
    [InlineData(-1f, 0.65f)]
    [InlineData(120f, 0f)]
    [InlineData(120f, 1.01f)]
    public void InvalidHapticTokensAreRejected(
        float frequencyHertz,
        float amplitude)
    {
        var settings = VRRecorderSettings.CreateDefault();
        var invalid = settings with
        {
            Vr = settings.Vr with
            {
                HapticFrequencyHertz = frequencyHertz,
                HapticAmplitude = amplitude,
            },
        };

        Assert.ThrowsAny<ArgumentException>(() =>
            VRRecorderSettingsContract.Validate(invalid));
    }

    [Fact]
    public void DuplicatePlacementProfileKeyIsRejected()
    {
        var defaults = VRRecorderSettings.CreateDefault();
        var device = new VrDeviceProfile(
            "lighthouse",
            "index-hmd",
            "/input/index_controller_profile.json");
        var profile = new VrOverlayPlacementProfile(
            device,
            VrHand.Left,
            OverlayPlacementMode.WristDock,
            new OverlayTransform([0, 0, 0], [0, 0, 0]));
        var invalid = defaults with
        {
            Vr = defaults.Vr with
            {
                PlacementProfiles = [profile, profile],
            },
        };

        Assert.Throws<InvalidDataException>(() =>
            VRRecorderSettingsContract.Validate(invalid));
    }

    [Theory]
    [InlineData(-96.001)]
    [InlineData(24.001)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void AudioGainOutsideMediaContractIsRejected(double gain)
    {
        var settings = VRRecorderSettings.CreateDefault();
        var invalid = settings with
        {
            Audio = settings.Audio with { DesktopGainDb = gain },
        };

        Assert.Throws<InvalidDataException>(() =>
            VRRecorderSettingsContract.Validate(invalid));
    }

    [Theory]
    [InlineData(RecordingMediaConfiguration.MinimumInputGainDb)]
    [InlineData(RecordingMediaConfiguration.MaximumInputGainDb)]
    public void AudioGainAtMediaContractBoundaryIsAccepted(double gain)
    {
        var settings = VRRecorderSettings.CreateDefault();
        var valid = settings with
        {
            Audio = settings.Audio with
            {
                DesktopGainDb = gain,
                MicrophoneGainDb = gain,
            },
        };

        VRRecorderSettingsContract.Validate(valid);
    }
}
