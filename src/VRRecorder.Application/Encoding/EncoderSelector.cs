using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

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
        return await SelectAsync(
                preference,
                vendor,
                encoder => new EncoderProbeRequest(
                    encoder,
                    adapterLuid: 0,
                    "unidentified-gpu"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EncoderKind> SelectAsync(
        EncoderPreference preference,
        StableVideoSignal signal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return await SelectAsync(
                preference,
                signal.GpuVendor,
                encoder => EncoderProbeRequest.ForSignal(encoder, signal),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EncoderKind> SelectAsync(
        EncoderPreference preference,
        GpuVendor candidateVendor,
        StableVideoSignal signal,
        int outputWidth,
        int outputHeight,
        FrameRate outputFrameRate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return await SelectAsync(
                preference,
                candidateVendor,
                encoder => EncoderProbeRequest.ForSignal(
                    encoder,
                    signal,
                    outputWidth,
                    outputHeight,
                    outputFrameRate),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EncoderKind> SelectAsync(
        EncoderPreference preference,
        StableVideoSignal signal,
        int outputWidth,
        int outputHeight,
        FrameRate outputFrameRate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return await SelectAsync(
                preference,
                signal.GpuVendor,
                encoder => EncoderProbeRequest.ForSignal(
                    encoder,
                    signal,
                    outputWidth,
                    outputHeight,
                    outputFrameRate),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<EncoderKind> SelectAsync(
        EncoderPreference preference,
        GpuVendor vendor,
        Func<EncoderKind, EncoderProbeRequest> createRequest,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in Candidates(preference, vendor))
        {
            var result = await _probe
                .ProbeAsync(createRequest(candidate), cancellationToken)
                .ConfigureAwait(false);
            if (result.IsPacketProduced)
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
