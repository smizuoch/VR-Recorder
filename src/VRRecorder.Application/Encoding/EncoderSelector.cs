using VRRecorder.Application.Ports;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Encoding;

public sealed class EncoderSelector
{
    private readonly IEncoderProbe _probe;

    public EncoderSelector(IEncoderProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public async Task<EncoderKind> SelectAsync(
        EncoderPreference preference,
        GpuVendor vendor,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in Candidates(preference, vendor))
        {
            var result = await _probe
                .ProbeAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            if (result == EncoderProbeResult.PacketProduced)
            {
                return candidate;
            }
        }

        throw new EncoderUnavailableException(preference);
    }

    private static IReadOnlyList<EncoderKind> Candidates(
        EncoderPreference preference,
        GpuVendor vendor) => preference switch
        {
            EncoderPreference.Auto => vendor switch
            {
                GpuVendor.Nvidia =>
                    [EncoderKind.Nvenc, EncoderKind.MediaFoundationSoftware],
                GpuVendor.Amd =>
                    [EncoderKind.Amf, EncoderKind.MediaFoundationSoftware],
                GpuVendor.Intel =>
                    [EncoderKind.Qsv, EncoderKind.MediaFoundationSoftware],
                GpuVendor.Unknown => [EncoderKind.MediaFoundationSoftware],
                _ => throw new ArgumentOutOfRangeException(
                    nameof(vendor),
                    vendor,
                    "Unknown GPU vendor."),
            },
            EncoderPreference.Nvenc => [EncoderKind.Nvenc],
            EncoderPreference.Amf => [EncoderKind.Amf],
            EncoderPreference.Qsv => [EncoderKind.Qsv],
            EncoderPreference.MediaFoundationSoftware =>
                [EncoderKind.MediaFoundationSoftware],
            _ => throw new ArgumentOutOfRangeException(
                nameof(preference),
                preference,
                "Unknown encoder preference."),
        };
}
