using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Encoding;

public sealed record EncoderProbeRequest
{
    public EncoderProbeRequest(
        EncoderKind encoder,
        ulong adapterLuid,
        string gpuIdentity)
    {
        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "The encoder kind is not defined.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gpuIdentity);
        Encoder = encoder;
        AdapterLuid = adapterLuid;
        GpuIdentity = gpuIdentity;
    }

    public EncoderKind Encoder { get; }

    public ulong AdapterLuid { get; }

    public string GpuIdentity { get; }

    public static EncoderProbeRequest ForSignal(
        EncoderKind encoder,
        StableVideoSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return new EncoderProbeRequest(
            encoder,
            signal.AdapterLuid,
            signal.GpuIdentity);
    }
}
