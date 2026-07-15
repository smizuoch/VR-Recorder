namespace VRRecorder.Application.Encoding;

public sealed record EncoderProbeResult
{
    private EncoderProbeResult(
        bool isPacketProduced,
        EncoderProbeEvidence? evidence)
    {
        IsPacketProduced = isPacketProduced;
        Evidence = evidence;
    }

    public static EncoderProbeResult Failed { get; } = new(false, null);

    public static EncoderProbeResult PacketProduced { get; } = new(true, null);

    public bool IsPacketProduced { get; }

    public EncoderProbeEvidence? Evidence { get; }

    public static EncoderProbeResult Verified(EncoderProbeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new EncoderProbeResult(true, evidence);
    }
}
