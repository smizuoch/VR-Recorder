using System.Net;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class WindowsDnsSdOscQueryServiceBrowserTests
{
    [Fact]
    public async Task DefaultBrowserFailsClosedOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var browser = new WindowsDnsSdOscQueryServiceBrowser();

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            browser.BrowseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FiltersAndOrdersDistinctOscJsonServicesBeforeMapping()
    {
        var alphaId = "VRChat-Client-alpha._oscjson._tcp.local.";
        var zetaId = "VRChat-Client-zeta._oscjson._tcp.local.";
        var api = new StubWindowsDnsSdApi(
        [
            zetaId,
            "ignored._osc._udp.local.",
            alphaId,
            zetaId,
        ],
        new Dictionary<string, WindowsDnsSdResolvedService>(
            StringComparer.OrdinalIgnoreCase)
        {
            [alphaId] = Resolved(
                alphaId,
                "alpha-host.local.",
                [IPAddress.IPv6Loopback, IPAddress.Loopback],
                port: 19001,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = "alpha",
                    ["txtvers"] = "1",
                }),
            [zetaId] = Resolved(
                zetaId,
                "zeta-host.local.",
                [IPAddress.Loopback],
                port: 19002,
                new Dictionary<string, string>(StringComparer.Ordinal)),
        });
        var browser = new WindowsDnsSdOscQueryServiceBrowser(api);

        var advertisements = await browser.BrowseAsync(CancellationToken.None);

        Assert.Equal("_oscjson._tcp.local", api.BrowseQuery);
        Assert.Equal([alphaId, zetaId], api.ResolveQueries);
        Assert.Collection(
            advertisements,
            advertisement =>
            {
                Assert.Equal(alphaId, advertisement.ServiceId);
                Assert.Equal("VRChat-Client-alpha", advertisement.InstanceName);
                Assert.Equal(IPAddress.Loopback, advertisement.Address);
                Assert.Equal(19001, advertisement.HttpPort);
            },
            advertisement =>
            {
                Assert.Equal(zetaId, advertisement.ServiceId);
                Assert.Equal("VRChat-Client-zeta", advertisement.InstanceName);
                Assert.Equal(IPAddress.Loopback, advertisement.Address);
                Assert.Equal(19002, advertisement.HttpPort);
            });
    }

    private static WindowsDnsSdResolvedService Resolved(
        string serviceInstanceName,
        string hostName,
        IReadOnlyList<IPAddress> addresses,
        int port,
        IReadOnlyDictionary<string, string> textProperties) =>
        new(
            serviceInstanceName,
            hostName,
            addresses,
            port,
            textProperties);

    private sealed class StubWindowsDnsSdApi : IWindowsDnsSdApi
    {
        private readonly IReadOnlyList<string> _browseResults;
        private readonly IReadOnlyDictionary<string, WindowsDnsSdResolvedService>
            _resolveResults;

        public StubWindowsDnsSdApi(
            IReadOnlyList<string> browseResults,
            IReadOnlyDictionary<string, WindowsDnsSdResolvedService>
                resolveResults)
        {
            _browseResults = browseResults;
            _resolveResults = resolveResults;
        }

        public bool IsSupported => true;

        public string? BrowseQuery { get; private set; }

        public List<string> ResolveQueries { get; } = [];

        public Task<IReadOnlyList<string>> BrowseAsync(
            string queryName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BrowseQuery = queryName;
            return Task.FromResult(_browseResults);
        }

        public Task<WindowsDnsSdResolvedService?> ResolveAsync(
            string serviceInstanceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveQueries.Add(serviceInstanceName);
            return Task.FromResult<WindowsDnsSdResolvedService?>(
                _resolveResults[serviceInstanceName]);
        }
    }
}
