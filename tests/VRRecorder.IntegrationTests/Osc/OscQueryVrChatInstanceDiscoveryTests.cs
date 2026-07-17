using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Diagnostics;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
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
    public void RejectsNonPositiveOrInfiniteDiscoveryTimeout()
    {
        using var invoker = new HttpMessageInvoker(
            new NeverCompletingHttpHandler());
        foreach (var timeout in new[]
                 {
                     TimeSpan.Zero,
                     TimeSpan.FromMilliseconds(-1),
                     Timeout.InfiniteTimeSpan,
                 })
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OscQueryVrChatInstanceDiscovery(
                    new StubOscQueryServiceBrowser([]),
                    invoker,
                    timeout));
            Assert.Equal("timeout", exception.ParamName);
        }
    }

    [Fact]
    public async Task IneligibleAdvertisementsAreIgnoredWithoutHttpProbe()
    {
        var wrongPrefix = new OscQueryServiceAdvertisement(
            "Other._oscjson._tcp.local.",
            "Other",
            IPAddress.Loopback,
            19020);
        var external = new OscQueryServiceAdvertisement(
            "VRChat-Client-external._oscjson._tcp.local.",
            "VRChat-Client-external",
            IPAddress.Parse("203.0.113.10"),
            19021);
        var browser = new StubOscQueryServiceBrowser(
            [null!, wrongPrefix, external]);
        using var invoker = new HttpMessageInvoker(
            new RejectEveryHttpRequestHandler());
        var events = new CapturingOscOperationEventSink();
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1),
            events);

        var candidates = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(candidates);
        Assert.Equal(
            [new OscOperationEvent(
                OscOperation.CapabilityProbe,
                OscOperationOutcome.Failed)],
            events.Events);
    }

    [Fact]
    public async Task DiagnosticSinkFailureCannotDiscardValidCandidate()
    {
        var advertisement = Advertisement("sink-failure", httpPort: 19022);
        var browser = new StubOscQueryServiceBrowser([advertisement]);
        using var invoker = new HttpMessageInvoker(
            new OscQueryFixtureHandler(new Dictionary<int, Fixture>
            {
                [advertisement.HttpPort] = new Fixture(
                    advertisement.InstanceName,
                    OscPort: 9022),
            }));
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1),
            new ThrowingOscOperationEventSink());

        var candidate = Assert.Single(await discovery.DiscoverAsync(
            CancellationToken.None));

        Assert.Equal(advertisement.ServiceId, candidate.ServiceId);
    }

    [Theory]
    [InlineData("missing-host-info")]
    [InlineData("host-info-not-object")]
    [InlineData("missing-name")]
    [InlineData("blank-name")]
    [InlineData("missing-osc-ip")]
    [InlineData("invalid-osc-ip")]
    [InlineData("external-osc-ip")]
    [InlineData("missing-osc-port")]
    [InlineData("osc-port-not-number")]
    [InlineData("osc-port-zero")]
    [InlineData("osc-port-too-large")]
    [InlineData("transport-not-string")]
    [InlineData("transport-not-udp")]
    public async Task InvalidHostInfoProducesNoCandidate(string mutation)
    {
        var advertisement = Advertisement("invalid-host", httpPort: 19023);
        var fixture = new Fixture(
            advertisement.InstanceName,
            OscPort: 9023,
            HostInfoJson: InvalidHostInfoJson(
                mutation,
                advertisement.InstanceName));
        using var invoker = new HttpMessageInvoker(
            new OscQueryFixtureHandler(new Dictionary<int, Fixture>
            {
                [advertisement.HttpPort] = fixture,
            }));
        var discovery = new OscQueryVrChatInstanceDiscovery(
            new StubOscQueryServiceBrowser([advertisement]),
            invoker,
            TimeSpan.FromSeconds(1));

        var candidates = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    [Trait("Scenario", "IT-021")]
    public async Task MultipleTargetsCreateAndSendNothingUntilExactSelection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var firstOsc = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var secondOsc = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        var first = Advertisement("select-alpha", httpPort: 19007);
        var second = Advertisement("select-beta", httpPort: 19008);
        var browser = new StubOscQueryServiceBrowser([second, first]);
        var oscQuery = new OscQueryFixtureHandler(
            new Dictionary<int, Fixture>
            {
                [first.HttpPort] = new Fixture(
                    first.InstanceName,
                    ((IPEndPoint)firstOsc.Client.LocalEndPoint!).Port),
                [second.HttpPort] = new Fixture(
                    second.InstanceName,
                    ((IPEndPoint)secondOsc.Client.LocalEndPoint!).Port),
            });
        using var invoker = new HttpMessageInvoker(oscQuery);
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1));
        var gateways = new CapturingGatewayFactory(
            new ConfirmedUdpVrChatCameraGatewayFactory(invoker));
        var useCase = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(discovery),
            gateways);

        var unresolved = await useCase.ResolveAsync(
            selectedServiceId: null,
            timeout.Token);
        var stale = await useCase.ResolveAsync(
            selectedServiceId: "stale-service-id",
            timeout.Token);

        Assert.IsType<
            VrChatCameraConnectionResolution.SelectionRequired>(unresolved);
        Assert.IsType<
            VrChatCameraConnectionResolution.SelectionRequired>(stale);
        Assert.Empty(gateways.CreatedFor);
        Assert.Equal(0, firstOsc.Available);
        Assert.Equal(0, secondOsc.Available);

        var resolved = await useCase.ResolveAsync(
            selectedServiceId: second.ServiceId,
            timeout.Token);

        var connected = Assert.IsType<
            VrChatCameraConnectionResolution.Connected>(resolved);
        Assert.Equal(second.ServiceId, connected.Candidate.ServiceId);
        Assert.Equal(new[] { connected.Candidate }, gateways.CreatedFor);
        Assert.Equal(0, firstOsc.Available);
        Assert.Equal(0, secondOsc.Available);

        await using var gatewayLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(
            connected.Gateway);
        var controller = new CameraSessionController(
            connected.Gateway,
            new InMemoryCameraLeaseStore());
        var snapshot = await connected.Gateway.ReadSnapshotAsync(timeout.Token);
        var acquisition = controller.AcquireAsync(
            snapshot,
            timeout.Token);

        var mode = await secondOsc.ReceiveAsync(timeout.Token);
        Assert.Equal(OscPacketCodec.EncodeMode(CameraMode.Stream), mode.Buffer);
        await secondOsc.SendAsync(
            mode.Buffer,
            mode.RemoteEndPoint,
            timeout.Token);
        var streaming = await secondOsc.ReceiveAsync(timeout.Token);
        Assert.Equal(OscPacketCodec.EncodeStreaming(true), streaming.Buffer);
        await secondOsc.SendAsync(
            streaming.Buffer,
            streaming.RemoteEndPoint,
            timeout.Token);
        await acquisition;

        Assert.Equal(0, firstOsc.Available);
        Assert.Equal(
            6,
            oscQuery.Requests.Count(request =>
                request.Port == first.HttpPort &&
                request.PathAndQuery is "/usercamera/Mode" or
                    "/usercamera/Streaming"));
        Assert.Equal(
            8,
            oscQuery.Requests.Count(request =>
                request.Port == second.HttpPort &&
                request.PathAndQuery is "/usercamera/Mode" or
                    "/usercamera/Streaming"));
    }

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
        var events = new CapturingOscOperationEventSink();
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1),
            events);
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
        Assert.Equal(
            [new OscOperationEvent(
                OscOperation.CapabilityProbe,
                OscOperationOutcome.Succeeded)],
            events.Events);
    }

    [Fact]
    public async Task SelectedGatewayRejectsSnapshotAfterOscQueryIdentityChanges()
    {
        var advertisement = Advertisement("identity-swap", httpPort: 19010);
        using var osc = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var handler = new OscQueryFixtureHandler(new Dictionary<int, Fixture>
        {
            [advertisement.HttpPort] = new Fixture(
                advertisement.InstanceName,
                ((IPEndPoint)osc.Client.LocalEndPoint!).Port),
        });
        using var invoker = new HttpMessageInvoker(handler);
        var connections = new VrChatCameraConnectionUseCase(
            new VrChatTargetResolver(new OscQueryVrChatInstanceDiscovery(
                new StubOscQueryServiceBrowser([advertisement]),
                invoker,
                TimeSpan.FromSeconds(1))),
            new ConfirmedUdpVrChatCameraGatewayFactory(invoker));
        var resolution = await connections.ResolveAsync(
            advertisement.ServiceId,
            CancellationToken.None);
        var connected = Assert.IsType<
            VrChatCameraConnectionResolution.Connected>(resolution);
        await using var gatewayLifetime = Assert.IsAssignableFrom<IAsyncDisposable>(
            connected.Gateway);
        handler.HostInfoNameOverrides[advertisement.HttpPort] =
            "VRChat-Client-different-service";

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            connected.Gateway.ReadSnapshotAsync(CancellationToken.None));

        Assert.Equal(0, osc.Available);
    }

    [Fact]
    public async Task DuplicateSecurityRelevantJsonPropertyIsRejected()
    {
        var advertisement = Advertisement("duplicate", httpPort: 19003);
        var fixture = new Fixture(
            advertisement.InstanceName,
            OscPort: 9020,
            HostInfoJson: $$"""
                {
                  "HOST_INFO": {
                    "NAME": "{{advertisement.InstanceName}}",
                    "OSC_IP": "203.0.113.10",
                    "OSC_IP": "127.0.0.1",
                    "OSC_PORT": 9020,
                    "OSC_TRANSPORT": "UDP"
                  }
                }
                """);
        var browser = new StubOscQueryServiceBrowser([advertisement]);
        using var invoker = new HttpMessageInvoker(
            new OscQueryFixtureHandler(new Dictionary<int, Fixture>
            {
                [advertisement.HttpPort] = fixture,
            }));
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            discovery.DiscoverAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CapabilityRedirectOutsideLoopbackProducesNoCandidate()
    {
        var advertisement = Advertisement("redirect", httpPort: 19009);
        var browser = new StubOscQueryServiceBrowser([advertisement]);
        using var invoker = new HttpMessageInvoker(
            new OscQueryFixtureHandler(new Dictionary<int, Fixture>
            {
                [advertisement.HttpPort] = new Fixture(
                    advertisement.InstanceName,
                    OscPort: 9040,
                    RedirectedPath: "/usercamera/Streaming",
                    EffectiveRequestUri: new Uri(
                        "http://203.0.113.10:19009/usercamera/Streaming")),
            }));
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1));

        var candidates = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task MissingCameraEndpointReturnsExplicitCapabilityFailure()
    {
        var advertisement = Advertisement("missing", httpPort: 19004);
        var browser = new StubOscQueryServiceBrowser([advertisement]);
        using var invoker = new HttpMessageInvoker(
            new OscQueryFixtureHandler(new Dictionary<int, Fixture>
            {
                [advertisement.HttpPort] = new Fixture(
                    advertisement.InstanceName,
                    OscPort: 9030,
                    MissingPath: "/usercamera/Streaming"),
            }));
        var events = new CapturingOscOperationEventSink();
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1),
            events);

        var exception = await Assert.ThrowsAsync<
            VrChatCameraEndpointMissingException>(() =>
            discovery.DiscoverAsync(CancellationToken.None));

        Assert.Equal(advertisement.ServiceId, exception.ServiceId);
        Assert.Equal("/usercamera/Streaming", exception.EndpointPath);
        Assert.Equal(
            [new OscOperationEvent(
                OscOperation.CapabilityProbe,
                OscOperationOutcome.Failed)],
            events.Events);
    }

    [Fact]
    public async Task InternalDeadlineReturnsExplicitTimeoutFailure()
    {
        var advertisement = Advertisement("timeout", httpPort: 19005);
        var browser = new StubOscQueryServiceBrowser([advertisement]);
        using var invoker = new HttpMessageInvoker(
            new NeverCompletingHttpHandler());
        var expectedTimeout = TimeSpan.FromMilliseconds(25);
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            expectedTimeout);

        var exception = await Assert.ThrowsAsync<OscQueryTimeoutException>(() =>
            discovery.DiscoverAsync(CancellationToken.None));

        Assert.Equal(expectedTimeout, exception.Timeout);
    }

    [Fact]
    public async Task CallerCancellationIsNotRemappedToTimeout()
    {
        var advertisement = Advertisement("cancel", httpPort: 19006);
        var browser = new StubOscQueryServiceBrowser([advertisement]);
        using var invoker = new HttpMessageInvoker(
            new NeverCompletingHttpHandler());
        var discovery = new OscQueryVrChatInstanceDiscovery(
            browser,
            invoker,
            TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.DiscoverAsync(cancellation.Token));

        Assert.IsNotType<OscQueryTimeoutException>(exception);
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

    private static string InvalidHostInfoJson(
        string mutation,
        string expectedName)
    {
        var body = mutation switch
        {
            "missing-host-info" => null,
            "host-info-not-object" => "[]",
            "missing-name" => """
                { "OSC_IP": "127.0.0.1", "OSC_PORT": 9023 }
                """,
            "blank-name" => """
                { "NAME": " ", "OSC_IP": "127.0.0.1", "OSC_PORT": 9023 }
                """,
            "missing-osc-ip" => $$"""
                { "NAME": "{{expectedName}}", "OSC_PORT": 9023 }
                """,
            "invalid-osc-ip" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "invalid", "OSC_PORT": 9023 }
                """,
            "external-osc-ip" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "203.0.113.10", "OSC_PORT": 9023 }
                """,
            "missing-osc-port" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "127.0.0.1" }
                """,
            "osc-port-not-number" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "127.0.0.1", "OSC_PORT": "9023" }
                """,
            "osc-port-zero" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "127.0.0.1", "OSC_PORT": 0 }
                """,
            "osc-port-too-large" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "127.0.0.1", "OSC_PORT": 65536 }
                """,
            "transport-not-string" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "127.0.0.1", "OSC_PORT": 9023, "OSC_TRANSPORT": 1 }
                """,
            "transport-not-udp" => $$"""
                { "NAME": "{{expectedName}}", "OSC_IP": "127.0.0.1", "OSC_PORT": 9023, "OSC_TRANSPORT": "TCP" }
                """,
            _ => throw new InvalidOperationException(mutation),
        };
        return body is null
            ? "{}"
            : $$"""
                { "HOST_INFO": {{body}} }
                """;
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

    private sealed class CapturingGatewayFactory : IVrChatCameraGatewayFactory
    {
        private readonly IVrChatCameraGatewayFactory _inner;

        public CapturingGatewayFactory(IVrChatCameraGatewayFactory inner)
        {
            _inner = inner;
        }

        public List<VrChatInstanceCandidate> CreatedFor { get; } = [];

        public IVrChatCameraGateway Create(VrChatInstanceCandidate candidate)
        {
            CreatedFor.Add(candidate);
            return _inner.Create(candidate);
        }
    }

    private sealed class InMemoryCameraLeaseStore : ICameraLeaseStore
    {
        public Task SaveAsync(
            CameraLease lease,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(
            CameraLease lease,
            CancellationToken cancellationToken) => Task.CompletedTask;
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

        public ConcurrentDictionary<int, string> HostInfoNameOverrides
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
            var hostInfoName = HostInfoNameOverrides.TryGetValue(
                uri.Port,
                out var overrideName)
                ? overrideName
                : fixture.Name;
            if (string.Equals(
                    uri.AbsolutePath,
                    fixture.MissingPath,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                    Content = new StringContent(
                        "{}",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            var json = uri.PathAndQuery switch
            {
                "/?HOST_INFO" => fixture.HostInfoJson ?? $$"""
                    {
                      "HOST_INFO": {
                        "NAME": "{{hostInfoName}}",
                        "OSC_IP": "127.0.0.1",
                        "OSC_PORT": {{fixture.OscPort}},
                        "OSC_TRANSPORT": "UDP"
                      }
                    }
                    """,
                "/usercamera/Mode" => Endpoint(
                    "/usercamera/Mode",
                    "i",
                    "\"VALUE\": [1]"),
                "/usercamera/Streaming" => Endpoint(
                    "/usercamera/Streaming",
                    "F"),
                "/usercamera/OrientationIsLandscape" => Endpoint(
                    "/usercamera/OrientationIsLandscape",
                    "T"),
                _ => throw new InvalidOperationException(
                    $"Unexpected OSCQuery request {uri.PathAndQuery}."),
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = string.Equals(
                    uri.AbsolutePath,
                    fixture.RedirectedPath,
                    StringComparison.Ordinal)
                    ? new HttpRequestMessage(
                        HttpMethod.Get,
                        fixture.EffectiveRequestUri)
                    : request,
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"),
            };
            return Task.FromResult(response);
        }

        private static string Endpoint(
            string path,
            string type,
            string? value = null) => $$"""
            {
              "FULL_PATH": "{{path}}",
              "TYPE": "{{type}}",
              "ACCESS": 3{{(value is null ? string.Empty : $",\n  {value}")}}
            }
            """;
    }

    private sealed class NeverCompletingHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException(
                "An infinite OSCQuery request unexpectedly completed.");
        }
    }

    private sealed class RejectEveryHttpRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "An ineligible advertisement reached the HTTP probe.");
    }

    private sealed class CapturingOscOperationEventSink
        : IOscOperationEventSink
    {
        public List<OscOperationEvent> Events { get; } = [];

        public void Publish(OscOperationEvent operation) =>
            Events.Add(operation);
    }

    private sealed class ThrowingOscOperationEventSink
        : IOscOperationEventSink
    {
        public void Publish(OscOperationEvent operation) =>
            throw new InvalidOperationException("diagnostics unavailable");
    }

    private sealed record Fixture(
        string Name,
        int OscPort,
        string? HostInfoJson = null,
        string? MissingPath = null,
        string? RedirectedPath = null,
        Uri? EffectiveRequestUri = null);
}
