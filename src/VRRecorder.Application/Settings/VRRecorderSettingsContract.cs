using System.Net;
using VRRecorder.Application.Haptics;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Settings;

public static class VRRecorderSettingsContract
{
    public static void Validate(VRRecorderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != VRRecorderSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Settings schema {settings.SchemaVersion} is not supported.");
        }

        ArgumentNullException.ThrowIfNull(settings.Recording);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            settings.Recording.OutputFolder);
        _ = SelfTimer.FromSeconds(settings.Recording.SelfTimerSeconds);
        if (settings.Recording.AutoStopSeconds is { } autoStopSeconds)
        {
            _ = RecordingDuration.FromSeconds(autoStopSeconds);
        }

        EnsureDefined(settings.Recording.ResolutionChangePolicy);

        ArgumentNullException.ThrowIfNull(settings.Video);
        _ = new FrameRate(settings.Video.FrameRate);
        EnsureDefined(settings.Video.Encoder);
        EnsureDefined(settings.Video.QualityPreset);
        EnsureDefined(settings.Video.Codec);

        ArgumentNullException.ThrowIfNull(settings.Audio);
        EnsureDefined(settings.Audio.Routing);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            settings.Audio.DesktopEndpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            settings.Audio.MicrophoneEndpointId);
        EnsureGain(settings.Audio.DesktopGainDb, "desktop gain");
        EnsureGain(settings.Audio.MicrophoneGainDb, "microphone gain");

        ArgumentNullException.ThrowIfNull(settings.Vr);
        EnsureDefined(settings.Vr.Hand);
        EnsureDefined(settings.Vr.PlacementMode);
        WristOverlayPoseContract.ValidateStoredTransform(settings.Vr.Transform);
        ArgumentNullException.ThrowIfNull(settings.Vr.PlacementProfiles);
        _ = new WristHapticFeedbackOptions(
            settings.Vr.HapticsEnabled,
            settings.Vr.HapticFrequencyHertz,
            settings.Vr.HapticAmplitude);
        if (settings.Vr.PlacementProfiles.Count > 64)
        {
            throw new InvalidDataException(
                "At most 64 VR overlay placement profiles may be stored.");
        }

        var profileKeys = new HashSet<(
            string TrackingSystem,
            string Hmd,
            string Controller,
            VrHand Hand)>();
        foreach (var profile in settings.Vr.PlacementProfiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(profile.Device);
            EnsureProfileIdentity(
                profile.Device.TrackingSystemName,
                "tracking system name");
            EnsureProfileIdentity(
                profile.Device.HmdModelNumber,
                "HMD model number");
            EnsureProfileIdentity(
                profile.Device.ControllerInputProfilePath,
                "controller input profile path");
            EnsureDefined(profile.Hand);
            EnsureDefined(profile.PlacementMode);
            WristOverlayPoseContract.ValidateStoredTransform(profile.Transform);
            if (!profileKeys.Add((
                    profile.Device.TrackingSystemName,
                    profile.Device.HmdModelNumber,
                    profile.Device.ControllerInputProfilePath,
                    profile.Hand)))
            {
                throw new InvalidDataException(
                    "VR overlay placement profile keys must be unique.");
            }
        }

        ArgumentNullException.ThrowIfNull(settings.Osc);
        if (!IPAddress.TryParse(settings.Osc.FallbackHost, out var address) ||
            !IPAddress.IsLoopback(address))
        {
            throw new InvalidDataException(
                "The fallback OSC host must be a loopback IP address.");
        }

        EnsurePort(settings.Osc.FallbackSendPort, "fallback send port");
        EnsurePort(settings.Osc.FallbackReceivePort, "fallback receive port");
        EnsureDefined(settings.UiLocale);
    }

    private static void EnsureDefined<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new InvalidDataException(
                $"Unknown {typeof(TEnum).Name} value {value}.");
        }
    }

    private static void EnsureGain(double value, string name)
    {
        if (!double.IsFinite(value) ||
            value is < RecordingMediaConfiguration.MinimumInputGainDb or
                > RecordingMediaConfiguration.MaximumInputGainDb)
        {
            throw new InvalidDataException(
                $"The {name} must be between " +
                $"{RecordingMediaConfiguration.MinimumInputGainDb} and " +
                $"{RecordingMediaConfiguration.MaximumInputGainDb} dB.");
        }
    }

    private static void EnsurePort(int port, string name)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidDataException(
                $"The {name} must be between 1 and 65535.");
        }
    }

    private static void EnsureProfileIdentity(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 512)
        {
            throw new InvalidDataException(
                $"The VR device {name} must be at most 512 characters.");
        }
    }
}
