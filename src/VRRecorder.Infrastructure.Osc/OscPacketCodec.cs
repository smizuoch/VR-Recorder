using System.Buffers.Binary;
using System.Text;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Infrastructure.Osc;

public static class OscPacketCodec
{
    private const string ModeAddress = "/usercamera/Mode";
    private const string StreamingAddress = "/usercamera/Streaming";

    public static byte[] EncodeMode(CameraMode mode)
    {
        var addressLength = PaddedStringLength(ModeAddress);
        var typeTagLength = PaddedStringLength(",i");
        var packet = new byte[addressLength + typeTagLength + sizeof(int)];
        WritePaddedString(packet, 0, ModeAddress);
        WritePaddedString(packet, addressLength, ",i");
        BinaryPrimitives.WriteInt32BigEndian(
            packet.AsSpan(addressLength + typeTagLength, sizeof(int)),
            (int)mode);
        return packet;
    }

    public static byte[] EncodeStreaming(bool enabled)
    {
        var addressLength = PaddedStringLength(StreamingAddress);
        var typeTag = enabled ? ",T" : ",F";
        var typeTagLength = PaddedStringLength(typeTag);
        var packet = new byte[addressLength + typeTagLength];
        WritePaddedString(packet, 0, StreamingAddress);
        WritePaddedString(packet, addressLength, typeTag);
        return packet;
    }

    private static int PaddedStringLength(string value)
    {
        var terminatedLength = Encoding.UTF8.GetByteCount(value) + 1;
        return (terminatedLength + 3) & ~3;
    }

    private static void WritePaddedString(
        byte[] destination,
        int offset,
        string value) =>
        Encoding.UTF8.GetBytes(value, destination.AsSpan(offset));
}
