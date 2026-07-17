using System.Text;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Recording;

public sealed record RecordingMediaConfiguration
{
    public const double DefaultInputGainDb = -6.0;
    public const double MinimumInputGainDb = -96.0;
    public const double MaximumInputGainDb = 24.0;
    private const int MaximumIdentityUtf8Bytes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public RecordingMediaConfiguration(
        AudioRouting audioRouting,
        string desktopEndpointId,
        string microphoneEndpointId,
        double desktopGainDb,
        double microphoneGainDb,
        VideoQualityPreset qualityPreset,
        string spoutSenderIdentity,
        ulong spoutAdapterLuid,
        ulong encoderAdapterLuid,
        string gpuIdentity)
    {
        EnsureDefined(audioRouting, nameof(audioRouting));
        EnsureDefined(qualityPreset, nameof(qualityPreset));
        EnsureText(desktopEndpointId, nameof(desktopEndpointId));
        EnsureText(microphoneEndpointId, nameof(microphoneEndpointId));
        EnsureGain(desktopGainDb, nameof(desktopGainDb));
        EnsureGain(microphoneGainDb, nameof(microphoneGainDb));
        EnsureText(spoutSenderIdentity, nameof(spoutSenderIdentity));
        EnsureText(gpuIdentity, nameof(gpuIdentity));

        AudioRouting = audioRouting;
        DesktopEndpointId = desktopEndpointId;
        MicrophoneEndpointId = microphoneEndpointId;
        DesktopGainDb = desktopGainDb;
        MicrophoneGainDb = microphoneGainDb;
        QualityPreset = qualityPreset;
        SpoutSenderIdentity = spoutSenderIdentity;
        SpoutAdapterLuid = spoutAdapterLuid;
        EncoderAdapterLuid = encoderAdapterLuid;
        GpuIdentity = gpuIdentity;
    }

    public AudioRouting AudioRouting { get; }

    public string DesktopEndpointId { get; }

    public string MicrophoneEndpointId { get; }

    public double DesktopGainDb { get; }

    public double MicrophoneGainDb { get; }

    public VideoQualityPreset QualityPreset { get; }

    public string SpoutSenderIdentity { get; }

    public ulong SpoutAdapterLuid { get; }

    public ulong EncoderAdapterLuid { get; }

    public string GpuIdentity { get; }

    public static RecordingMediaConfiguration CreateDefault() =>
        new(
            AudioRouting.Mixed,
            "default-render",
            "default-capture",
            DefaultInputGainDb,
            DefaultInputGainDb,
            VideoQualityPreset.High,
            "unidentified-spout-sender",
            0,
            0,
            "unidentified-gpu");

    public RecordingMediaConfiguration WithVideoSource(
        StableVideoSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (!signal.HasDiscoveredSourceIdentity)
        {
            return this;
        }

        return new RecordingMediaConfiguration(
            AudioRouting,
            DesktopEndpointId,
            MicrophoneEndpointId,
            DesktopGainDb,
            MicrophoneGainDb,
            QualityPreset,
            signal.SenderId,
            signal.AdapterLuid,
            signal.AdapterLuid,
            signal.GpuIdentity);
    }

    public RecordingMediaConfiguration WithAudioRouting(
        AudioRouting audioRouting) => new(
        audioRouting,
        DesktopEndpointId,
        MicrophoneEndpointId,
        DesktopGainDb,
        MicrophoneGainDb,
        QualityPreset,
        SpoutSenderIdentity,
        SpoutAdapterLuid,
        EncoderAdapterLuid,
        GpuIdentity);

    private static void EnsureDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Unknown {typeof(TEnum).Name} value.");
        }
    }

    private static void EnsureText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Media identity text cannot contain control characters.",
                parameterName);
        }

        try
        {
            if (StrictUtf8.GetByteCount(value) > MaximumIdentityUtf8Bytes)
            {
                throw new ArgumentException(
                    $"Media identity text cannot exceed {MaximumIdentityUtf8Bytes} UTF-8 bytes.",
                    parameterName);
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Media identity text must be valid UTF-16 and UTF-8 encodable.",
                parameterName,
                exception);
        }
    }

    private static void EnsureGain(double value, string parameterName)
    {
        if (!double.IsFinite(value) ||
            value is < MinimumInputGainDb or > MaximumInputGainDb)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Input gain must be finite and between {MinimumInputGainDb} and {MaximumInputGainDb} dB.");
        }
    }
}
