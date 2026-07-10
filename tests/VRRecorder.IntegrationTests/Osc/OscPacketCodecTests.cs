using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class OscPacketCodecTests
{
    [Fact]
    public void StreamModeUsesOscInt32PacketBytes()
    {
        var packet = OscPacketCodec.EncodeMode(CameraMode.Stream);

        Assert.Equal(
            Convert.FromHexString(
                "2F7573657263616D6572612F4D6F6465000000002C69000000000002"),
            packet);
    }

    [Theory]
    [InlineData(
        true,
        "2F7573657263616D6572612F53747265616D696E670000002C540000")]
    [InlineData(
        false,
        "2F7573657263616D6572612F53747265616D696E670000002C460000")]
    public void StreamingUsesOscBooleanTypeTag(bool enabled, string expectedHex)
    {
        var packet = OscPacketCodec.EncodeStreaming(enabled);

        Assert.Equal(Convert.FromHexString(expectedHex), packet);
    }
}
