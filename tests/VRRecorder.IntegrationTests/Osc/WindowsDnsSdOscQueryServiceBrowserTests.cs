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

    [Theory]
    [InlineData(1460u)]
    [InlineData(9003u)]
    [InlineData(9501u)]
    [InlineData(9554u)]
    [InlineData(9701u)]
    [InlineData(9714u)]
    public async Task TransientResolveFailureDoesNotDiscardValidCandidate(
        uint status)
    {
        var alphaId = "VRChat-Client-alpha._oscjson._tcp.local.";
        var zetaId = "VRChat-Client-zeta._oscjson._tcp.local.";
        var api = new StubWindowsDnsSdApi(
            [alphaId, zetaId],
            new Dictionary<string, WindowsDnsSdResolvedService>(
                StringComparer.OrdinalIgnoreCase)
            {
                [zetaId] = Resolved(
                    zetaId,
                    "zeta-host.local.",
                    [IPAddress.Loopback],
                    port: 19002,
                    new Dictionary<string, string>(StringComparer.Ordinal)),
            },
            new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase)
            {
                [alphaId] = new WindowsDnsSdException("resolve", status),
            });
        var browser = new WindowsDnsSdOscQueryServiceBrowser(api);

        var advertisements = await browser.BrowseAsync(CancellationToken.None);

        var advertisement = Assert.Single(advertisements);
        Assert.Equal(zetaId, advertisement.ServiceId);
        Assert.Equal([alphaId, zetaId], api.ResolveQueries);
    }

    [Fact]
    public async Task StructuralResolveFailurePropagates()
    {
        var serviceId = "VRChat-Client-alpha._oscjson._tcp.local.";
        var failure = new WindowsDnsSdException("resolve", status: 13);
        var browser = CreateFailingBrowser(serviceId, failure);

        var actual = await Assert.ThrowsAsync<WindowsDnsSdException>(() =>
            browser.BrowseAsync(CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task UnclassifiedIoResolveFailurePropagates()
    {
        var serviceId = "VRChat-Client-alpha._oscjson._tcp.local.";
        var failure = new IOException("Unclassified resolve failure.");
        var browser = CreateFailingBrowser(serviceId, failure);

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            browser.BrowseAsync(CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task PlatformResolveFailurePropagates()
    {
        var serviceId = "VRChat-Client-alpha._oscjson._tcp.local.";
        var failure = new PlatformNotSupportedException();
        var browser = CreateFailingBrowser(serviceId, failure);

        var actual = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            browser.BrowseAsync(CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task CallerCancellationWinsOverTransientResolveFailure()
    {
        var serviceId = "VRChat-Client-alpha._oscjson._tcp.local.";
        using var cancellation = new CancellationTokenSource();
        var browser = CreateFailingBrowser(
            serviceId,
            new WindowsDnsSdException("resolve", status: 9554),
            _ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            browser.BrowseAsync(cancellation.Token));
    }

    private static WindowsDnsSdOscQueryServiceBrowser CreateFailingBrowser(
        string serviceId,
        Exception failure,
        Action<string>? beforeFailure = null) =>
        new(new StubWindowsDnsSdApi(
            [serviceId],
            new Dictionary<string, WindowsDnsSdResolvedService>(
                StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase)
            {
                [serviceId] = failure,
            },
            beforeFailure));

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
        private readonly IReadOnlyDictionary<string, Exception> _resolveFailures;
        private readonly Action<string>? _beforeFailure;

        public StubWindowsDnsSdApi(
            IReadOnlyList<string> browseResults,
            IReadOnlyDictionary<string, WindowsDnsSdResolvedService>
                resolveResults,
            IReadOnlyDictionary<string, Exception>? resolveFailures = null,
            Action<string>? beforeFailure = null)
        {
            _browseResults = browseResults;
            _resolveResults = resolveResults;
            _resolveFailures = resolveFailures ??
                new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);
            _beforeFailure = beforeFailure;
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
            if (_resolveFailures.TryGetValue(serviceInstanceName, out var failure))
            {
                _beforeFailure?.Invoke(serviceInstanceName);
                return Task.FromException<WindowsDnsSdResolvedService?>(failure);
            }

            return Task.FromResult<WindowsDnsSdResolvedService?>(
                _resolveResults[serviceInstanceName]);
        }
    }
}
