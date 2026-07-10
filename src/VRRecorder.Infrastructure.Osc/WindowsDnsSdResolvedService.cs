using System.Collections.ObjectModel;
using System.Net;

namespace VRRecorder.Infrastructure.Osc;

public sealed record WindowsDnsSdResolvedService
{
    public WindowsDnsSdResolvedService(
        string serviceInstanceName,
        string hostName,
        IReadOnlyList<IPAddress> addresses,
        int port,
        IReadOnlyDictionary<string, string> textProperties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceInstanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(textProperties);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                "The DNS-SD service port must be between 1 and 65535.");
        }

        ServiceInstanceName = serviceInstanceName;
        HostName = hostName;
        Addresses = addresses.ToArray();
        Port = port;
        TextProperties = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                textProperties,
                StringComparer.Ordinal));
    }

    public string ServiceInstanceName { get; }

    public string HostName { get; }

    public IReadOnlyList<IPAddress> Addresses { get; }

    public int Port { get; }

    public IReadOnlyDictionary<string, string> TextProperties { get; }
}
