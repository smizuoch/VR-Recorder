using System.Net;
using System.Net.Sockets;

namespace VRRecorder.Infrastructure.Osc;

public sealed class WindowsDnsSdOscQueryServiceBrowser
    : IOscQueryServiceBrowser
{
    private const string QueryName = "_oscjson._tcp.local";
    private const string ServiceSuffix = "._oscjson._tcp.local.";
    private const uint ErrorTimeout = 1460;
    private const uint DnsErrorRcodeNameError = 9003;
    private const uint DnsInfoNoRecords = 9501;
    private const uint DnsErrorTryAgainLater = 9554;
    private const uint DnsErrorRecordDoesNotExist = 9701;
    private const uint DnsErrorNameDoesNotExist = 9714;
    private readonly IWindowsDnsSdApi _api;

    public WindowsDnsSdOscQueryServiceBrowser()
        : this(new WindowsDnsSdApi())
    {
    }

    public WindowsDnsSdOscQueryServiceBrowser(IWindowsDnsSdApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public async Task<IReadOnlyList<OscQueryServiceAdvertisement>> BrowseAsync(
        CancellationToken cancellationToken)
    {
        if (!_api.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Windows DNS-SD discovery requires Windows 10 or later.");
        }

        var browseResults = await _api
            .BrowseAsync(QueryName, cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(browseResults);
        var serviceIds = browseResults
            .Where(result => !string.IsNullOrWhiteSpace(result))
            .Select(NormalizeServiceId)
            .Where(IsOscJsonService)
            .GroupBy(serviceId => serviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Order(StringComparer.Ordinal).First())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var advertisements = new List<OscQueryServiceAdvertisement>(
            serviceIds.Length);
        foreach (var serviceId in serviceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WindowsDnsSdResolvedService? resolved;
            try
            {
                resolved = await _api
                    .ResolveAsync(serviceId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (WindowsDnsSdException exception) when (
                IsIsolatedResolveFailure(exception))
            {
                continue;
            }

            var advertisement = TryMap(serviceId, resolved);
            if (advertisement is not null)
            {
                advertisements.Add(advertisement);
            }
        }

        return advertisements;
    }

    private static OscQueryServiceAdvertisement? TryMap(
        string serviceId,
        WindowsDnsSdResolvedService? resolved)
    {
        if (resolved is null ||
            !string.Equals(
                NormalizeServiceId(resolved.ServiceInstanceName),
                serviceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var address = resolved.Addresses
            .Where(candidate =>
                candidate.AddressFamily is AddressFamily.InterNetwork or
                    AddressFamily.InterNetworkV6 &&
                IPAddress.IsLoopback(candidate))
            .OrderBy(candidate =>
                candidate.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ThenBy(candidate => candidate.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();
        if (address is null)
        {
            return null;
        }

        var instanceName = serviceId[..^ServiceSuffix.Length];
        return new OscQueryServiceAdvertisement(
            serviceId,
            instanceName,
            address,
            resolved.Port);
    }

    private static string NormalizeServiceId(string serviceId)
    {
        var normalized = serviceId.Trim();
        return normalized.EndsWith('.')
            ? normalized
            : $"{normalized}.";
    }

    private static bool IsOscJsonService(string serviceId) =>
        serviceId.Length > ServiceSuffix.Length &&
        serviceId.EndsWith(ServiceSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsIsolatedResolveFailure(
        WindowsDnsSdException exception) =>
        string.Equals(
            exception.Operation,
            "resolve",
            StringComparison.Ordinal) &&
        exception.Status is
            ErrorTimeout or
            DnsErrorRcodeNameError or
            DnsInfoNoRecords or
            DnsErrorTryAgainLater or
            DnsErrorRecordDoesNotExist or
            DnsErrorNameDoesNotExist;
}
