using System.Net;
using System.Net.Sockets;

namespace VRRecorder.Infrastructure.Osc;

public sealed record OscQueryServiceAdvertisement
{
    public OscQueryServiceAdvertisement(
        string serviceId,
        string instanceName,
        IPAddress address,
        int httpPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily is not (
                AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            throw new ArgumentException(
                "Only IPv4 and IPv6 OSCQuery services are supported.",
                nameof(address));
        }

        if (httpPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(httpPort),
                httpPort,
                "The OSCQuery HTTP port must be between 1 and 65535.");
        }

        ServiceId = serviceId;
        InstanceName = instanceName;
        Address = address;
        HttpPort = httpPort;
    }

    public string ServiceId { get; }

    public string InstanceName { get; }

    public IPAddress Address { get; }

    public int HttpPort { get; }
}
