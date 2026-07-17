using System.Net;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class OscQueryServiceAdvertisementTests
{
    [Fact]
    public void AcceptsIpv6ServiceIdentity()
    {
        var advertisement = new OscQueryServiceAdvertisement(
            "VRChat-Client-v6._oscjson._tcp.local.",
            "VRChat-Client-v6",
            IPAddress.IPv6Loopback,
            19000);

        Assert.Equal(IPAddress.IPv6Loopback, advertisement.Address);
        Assert.Equal(19000, advertisement.HttpPort);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void RejectsPortOutsideTcpRange(int port)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OscQueryServiceAdvertisement(
                "VRChat-Client-test._oscjson._tcp.local.",
                "VRChat-Client-test",
                IPAddress.Loopback,
                port));

        Assert.Equal("httpPort", exception.ParamName);
    }
}
