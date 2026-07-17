using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

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

    [Fact]
    public void RejectsMalformedSectionsEnumsProfilesAndOscFallbacks()
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
            WristOverlayPoseContract.CreateDefaultWristDockTransform());
        var invalid = new VRRecorderSettings[]
        {
            defaults with { SchemaVersion = 2 },
            defaults with { Recording = null! },
            defaults with
            {
                Recording = defaults.Recording with { OutputFolder = " " },
            },
            defaults with
            {
                Recording = defaults.Recording with
                {
                    ResolutionChangePolicy =
                        (ResolutionChangePolicy)int.MaxValue,
                },
            },
            defaults with { Video = null! },
            defaults with
            {
                Video = defaults.Video with { FrameRate = 0 },
            },
            defaults with
            {
                Video = defaults.Video with
                {
                    Encoder = (EncoderPreference)int.MaxValue,
                },
            },
            defaults with
            {
                Video = defaults.Video with
                {
                    QualityPreset = (VideoQualityPreset)int.MaxValue,
                },
            },
            defaults with
            {
                Video = defaults.Video with { Codec = (VideoCodec)int.MaxValue },
            },
            defaults with { Audio = null! },
            defaults with
            {
                Audio = defaults.Audio with { Routing = (AudioRouting)int.MaxValue },
            },
            defaults with
            {
                Audio = defaults.Audio with { DesktopEndpointId = " " },
            },
            defaults with
            {
                Audio = defaults.Audio with { MicrophoneEndpointId = " " },
            },
            defaults with
            {
                Audio = defaults.Audio with
                {
                    MicrophoneGainDb = double.PositiveInfinity,
                },
            },
            defaults with { Vr = null! },
            defaults with
            {
                Vr = defaults.Vr with { Hand = (VrHand)int.MaxValue },
            },
            defaults with
            {
                Vr = defaults.Vr with
                {
                    PlacementMode = (OverlayPlacementMode)int.MaxValue,
                },
            },
            defaults with
            {
                Vr = defaults.Vr with { PlacementProfiles = null! },
            },
            defaults with
            {
                Vr = defaults.Vr with
                {
                    PlacementProfiles = Enumerable.Repeat(profile, 65).ToArray(),
                },
            },
            defaults with
            {
                Vr = defaults.Vr with { PlacementProfiles = [null!] },
            },
            defaults with
            {
                Vr = defaults.Vr with
                {
                    PlacementProfiles = [profile with { Device = null! }],
                },
            },
            defaults with
            {
                Vr = defaults.Vr with
                {
                    PlacementProfiles =
                    [
                        profile with
                        {
                            Device = device with
                            {
                                TrackingSystemName = new string('x', 513),
                            },
                        },
                    ],
                },
            },
            defaults with
            {
                Vr = defaults.Vr with
                {
                    PlacementProfiles =
                    [profile with { Hand = (VrHand)int.MaxValue }],
                },
            },
            defaults with { Osc = null! },
            defaults with
            {
                Osc = defaults.Osc with { FallbackHost = "192.0.2.1" },
            },
            defaults with
            {
                Osc = defaults.Osc with { FallbackHost = "invalid" },
            },
            defaults with
            {
                Osc = defaults.Osc with { FallbackSendPort = 0 },
            },
            defaults with
            {
                Osc = defaults.Osc with { FallbackReceivePort = 65_536 },
            },
            defaults with { UiLocale = (UiLocale)int.MaxValue },
        };

        Assert.Throws<ArgumentNullException>(() =>
            VRRecorderSettingsContract.Validate(null!));
        foreach (var settings in invalid)
        {
            var exception = Assert.ThrowsAny<Exception>(() =>
                VRRecorderSettingsContract.Validate(settings));
            Assert.True(exception is ArgumentException or InvalidDataException);
        }

        VRRecorderSettingsContract.Validate(defaults with
        {
            Recording = defaults.Recording with { AutoStopSeconds = 3 },
            Osc = defaults.Osc with { FallbackHost = "::1" },
            Vr = defaults.Vr with { PlacementProfiles = [profile] },
        });
    }
}
