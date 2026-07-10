using System.Collections.Concurrent;
using System.Net;
using System.Text;
using VRRecorder.Application.Camera;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class OscQueryVrChatInstanceDiscoveryTests
{
    private static readonly string[] ExpectedCapabilityPaths =
    [
        "/?HOST_INFO",
        "/usercamera/Mode",
        "/usercamera/OrientationIsLandscape",
        "/usercamera/Streaming",
    ];

    [Fact]
    public async Task MultipleValidLoopbackServicesRequireSelectionAfterCapabilityProbe()
    {
        var first = Advertisement("alpha", httpPort: 19001);
        var second = Advertisement("beta", httpPort: 19002);
        var browser = new StubOscQueryServiceBrowser([second, first]);
        var http = new OscQueryFixtureHandler(new Dictionary<int, Fixture>
        {
            [first.HttpPort] = new Fixture(first.InstanceName, OscPort: 9000),
            [second.HttpPort] = new Fixture(second.InstanceName, OscPort: 9010),
        });
        using var invoker = new HttpMessageInvoker(http);
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1));
        var resolver = new VrChatTargetResolver(discovery);

        var result = await resolver.ResolveAsync(
            selectedServiceId: null,
            CancellationToken.None);

        var selection = Assert.IsType<
            VrChatTargetResolution.SelectionRequired>(result);
        Assert.Collection(
            selection.Candidates,
            candidate => AssertCandidate(candidate, first, oscPort: 9000),
            candidate => AssertCandidate(candidate, second, oscPort: 9010));
        Assert.Equal(
            ExpectedCapabilityPaths,
            http.Requests
                .Where(request => request.Port == first.HttpPort)
                .Select(request => request.PathAndQuery)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            8,
            http.Requests.Count);
    }

    private static OscQueryServiceAdvertisement Advertisement(
        string suffix,
        int httpPort)
    {
        var instanceName = $"VRChat-Client-{suffix}";
        return new OscQueryServiceAdvertisement(
            serviceId: $"{instanceName}._oscjson._tcp.local.",
            instanceName: instanceName,
            address: IPAddress.Loopback,
            httpPort: httpPort);
    }

    private static void AssertCandidate(
        VrChatInstanceCandidate candidate,
        OscQueryServiceAdvertisement advertisement,
        int oscPort)
    {
        Assert.Equal(advertisement.ServiceId, candidate.ServiceId);
        Assert.Equal(advertisement.InstanceName, candidate.DisplayName);
        Assert.Equal(
            new Uri($"http://127.0.0.1:{advertisement.HttpPort}/"),
            candidate.OscQueryEndpoint);
        Assert.Equal("127.0.0.1", candidate.OscHost);
        Assert.Equal(oscPort, candidate.OscPort);
    }

    private sealed class StubOscQueryServiceBrowser
        : IOscQueryServiceBrowser
    {
        private readonly IReadOnlyList<OscQueryServiceAdvertisement> _services;

        public StubOscQueryServiceBrowser(
            IReadOnlyList<OscQueryServiceAdvertisement> services)
        {
            _services = services;
        }

        public Task<IReadOnlyList<OscQueryServiceAdvertisement>> BrowseAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_services);
        }
    }

    private sealed class OscQueryFixtureHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<int, Fixture> _fixtures;

        public OscQueryFixtureHandler(
            IReadOnlyDictionary<int, Fixture> fixtures)
        {
            _fixtures = fixtures;
        }

        public ConcurrentBag<(int Port, string PathAndQuery)> Requests
        { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri ??
                      throw new InvalidOperationException(
                          "The OSCQuery request URI is missing.");
            Requests.Add((uri.Port, uri.PathAndQuery));
            var fixture = _fixtures[uri.Port];
            var json = uri.PathAndQuery switch
            {
                "/?HOST_INFO" => $$"""
                    {
                      "HOST_INFO": {
                        "NAME": "{{fixture.Name}}",
                        "OSC_IP": "127.0.0.1",
                        "OSC_PORT": {{fixture.OscPort}},
                        "OSC_TRANSPORT": "UDP"
                      }
                    }
                    """,
                "/usercamera/Mode" => Endpoint(
                    "/usercamera/Mode",
                    "i"),
                "/usercamera/Streaming" => Endpoint(
                    "/usercamera/Streaming",
                    "T"),
                "/usercamera/OrientationIsLandscape" => Endpoint(
                    "/usercamera/OrientationIsLandscape",
                    "T"),
                _ => throw new InvalidOperationException(
                    $"Unexpected OSCQuery request {uri.PathAndQuery}."),
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"),
            };
            return Task.FromResult(response);
        }

        private static string Endpoint(string path, string type) => $$"""
            {
              "FULL_PATH": "{{path}}",
              "TYPE": "{{type}}",
              "ACCESS": 3
            }
            """;
    }

    private sealed record Fixture(string Name, int OscPort);
}
