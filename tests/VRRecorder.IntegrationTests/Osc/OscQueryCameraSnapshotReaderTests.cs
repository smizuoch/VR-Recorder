using System.Net;
using System.Text;
using VRRecorder.Application.Camera;
using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class OscQueryCameraSnapshotReaderTests
{
    [Theory]
    [InlineData("https://127.0.0.1:19000/", "127.0.0.1")]
    [InlineData("http://localhost:19000/", "127.0.0.1")]
    [InlineData("http://user@127.0.0.1:19000/", "127.0.0.1")]
    [InlineData("http://127.0.0.1:19000/nested", "127.0.0.1")]
    [InlineData("http://127.0.0.1:19000/?query=1", "127.0.0.1")]
    [InlineData("http://127.0.0.1:19000/#fragment", "127.0.0.1")]
    [InlineData("http://127.0.0.1:19000/", "203.0.113.10")]
    [InlineData("http://127.0.0.1:19000/", "not-an-address")]
    public void RejectsUntrustedSelectedEndpoint(string endpoint, string oscHost)
    {
        using var invoker = new HttpMessageInvoker(new SnapshotHandler());
        var candidate = Candidate(new Uri(endpoint), oscHost);

        var exception = Assert.Throws<ArgumentException>(() =>
            new OscQueryCameraSnapshotReader(candidate, invoker));

        Assert.Equal("candidate", exception.ParamName);
    }

    [Theory]
    [InlineData("T", "[true]", true)]
    [InlineData("F", "[false]", false)]
    [InlineData("T", "[]", true)]
    public async Task ReadsCompatibleExplicitStreamingValues(
        string type,
        string value,
        bool expected)
    {
        using var invoker = new HttpMessageInvoker(new SnapshotHandler(
            streamingJson: Endpoint(
                "/usercamera/Streaming",
                type,
                $"\"VALUE\": {value}")));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        var snapshot = await reader.ReadAsync(CancellationToken.None);

        Assert.True(snapshot.Streaming.IsKnown);
        Assert.Equal(expected, snapshot.Streaming.Value);
    }

    [Theory]
    [InlineData("T", "{}")]
    [InlineData("T", "[true, false]")]
    [InlineData("T", "[1]")]
    [InlineData("F", "[true]")]
    public async Task RejectsInconsistentStreamingValues(
        string type,
        string value)
    {
        using var invoker = new HttpMessageInvoker(new SnapshotHandler(
            streamingJson: Endpoint(
                "/usercamera/Streaming",
                type,
                $"\"VALUE\": {value}")));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("missing-path")]
    [InlineData("wrong-path")]
    [InlineData("wrong-type")]
    [InlineData("read-only")]
    [InlineData("missing-value")]
    [InlineData("value-not-array")]
    [InlineData("multiple-values")]
    [InlineData("value-not-integer")]
    [InlineData("undefined-mode")]
    public async Task RejectsInvalidModeEndpointContract(string mutation)
    {
        using var invoker = new HttpMessageInvoker(new SnapshotHandler(
            modeJson: InvalidModeJson(mutation)));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://127.0.0.1:19000/?HOST_INFO")]
    [InlineData("http://203.0.113.10:19000/?HOST_INFO")]
    [InlineData("http://127.0.0.1:19001/?HOST_INFO")]
    [InlineData("http://127.0.0.1:19000/wrong")]
    public async Task RejectsUntrustedEffectiveResponseUri(string? effectiveUri)
    {
        using var invoker = new HttpMessageInvoker(new FirstResponseHandler(
            ValidHostInfoJson,
            "application/json",
            effectiveUri is null ? null : new Uri(effectiveUri)));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectsNonJsonSnapshotResponse()
    {
        using var invoker = new HttpMessageInvoker(new FirstResponseHandler(
            ValidHostInfoJson,
            "text/plain",
            new Uri("http://127.0.0.1:19000/?HOST_INFO")));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectsSnapshotResponseWithoutContentType()
    {
        using var content = new ByteArrayContent(
            Encoding.UTF8.GetBytes(ValidHostInfoJson));
        using var invoker = new HttpMessageInvoker(new FirstResponseHandler(
            content,
            new Uri("http://127.0.0.1:19000/?HOST_INFO")));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsDeclaredOrStreamedResponseAboveSixtyFourKiB(
        bool declareLength)
    {
        var bytes = new byte[(64 * 1024) + 1];
        HttpContent content = declareLength
            ? new ByteArrayContent(bytes)
            : new UnknownLengthContent(bytes);
        content.Headers.ContentType = new("application/json");
        using var invoker = new HttpMessageInvoker(new FirstResponseHandler(
            content,
            new Uri("http://127.0.0.1:19000/?HOST_INFO")));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("nested")]
    public async Task RejectsDuplicateSnapshotJsonProperties(string location)
    {
        var json = location == "root"
            ? $$"""
                {
                  "HOST_INFO": {{ValidHostInfoObject}},
                  "HOST_INFO": {{ValidHostInfoObject}}
                }
                """
            : """
                {
                  "HOST_INFO": {
                    "NAME": "VRChat-Client-test",
                    "NAME": "VRChat-Client-test",
                    "OSC_IP": "127.0.0.1",
                    "OSC_PORT": 9000
                  }
                }
                """;
        using var invoker = new HttpMessageInvoker(new FirstResponseHandler(
            json,
            "application/json",
            new Uri("http://127.0.0.1:19000/?HOST_INFO")));
        var reader = new OscQueryCameraSnapshotReader(Candidate(), invoker);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadAsync(CancellationToken.None));
    }

    private static VrChatInstanceCandidate Candidate(
        Uri? endpoint = null,
        string oscHost = "127.0.0.1") => new(
        "VRChat-Client-test._oscjson._tcp.local.",
        "VRChat-Client-test",
        endpoint ?? new Uri("http://127.0.0.1:19000/"),
        oscHost,
        9000);

    private const string ValidHostInfoObject = """
        {
          "NAME": "VRChat-Client-test",
          "OSC_IP": "127.0.0.1",
          "OSC_PORT": 9000,
          "OSC_TRANSPORT": "UDP"
        }
        """;

    private const string ValidHostInfoJson = $$"""
        {
          "HOST_INFO": {{ValidHostInfoObject}}
        }
        """;

    private static string InvalidModeJson(string mutation) => mutation switch
    {
        "missing-path" => Endpoint(null, "i", "\"VALUE\": [1]"),
        "wrong-path" => Endpoint(
            "/usercamera/Wrong",
            "i",
            "\"VALUE\": [1]"),
        "wrong-type" => Endpoint(
            "/usercamera/Mode",
            "f",
            "\"VALUE\": [1]"),
        "read-only" => Endpoint(
            "/usercamera/Mode",
            "i",
            "\"VALUE\": [1]",
            access: 1),
        "missing-value" => Endpoint("/usercamera/Mode", "i"),
        "value-not-array" => Endpoint(
            "/usercamera/Mode",
            "i",
            "\"VALUE\": {}"),
        "multiple-values" => Endpoint(
            "/usercamera/Mode",
            "i",
            "\"VALUE\": [1, 2]"),
        "value-not-integer" => Endpoint(
            "/usercamera/Mode",
            "i",
            "\"VALUE\": [\"1\"]"),
        "undefined-mode" => Endpoint(
            "/usercamera/Mode",
            "i",
            "\"VALUE\": [99]"),
        _ => throw new InvalidOperationException(mutation),
    };

    private static string Endpoint(
        string? path,
        string type,
        string? value = null,
        int access = 3)
    {
        var pathProperty = path is null
            ? string.Empty
            : $"\"FULL_PATH\": \"{path}\",";
        var valueProperty = value is null ? string.Empty : $", {value}";
        return $$"""
            {
              {{pathProperty}}
              "TYPE": "{{type}}",
              "ACCESS": {{access}}{{valueProperty}}
            }
            """;
    }

    private sealed class SnapshotHandler(
        string? modeJson = null,
        string? streamingJson = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.PathAndQuery;
            var json = path switch
            {
                "/?HOST_INFO" => """
                    {
                      "HOST_INFO": {
                        "NAME": "VRChat-Client-test",
                        "OSC_IP": "127.0.0.1",
                        "OSC_PORT": 9000,
                        "OSC_TRANSPORT": "UDP"
                      }
                    }
                    """,
                "/usercamera/Mode" => modeJson ?? Endpoint(
                    "/usercamera/Mode",
                    "i",
                    "\"VALUE\": [1]"),
                "/usercamera/Streaming" => streamingJson ?? Endpoint(
                    "/usercamera/Streaming",
                    "F"),
                _ => throw new InvalidOperationException(path),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class FirstResponseHandler : HttpMessageHandler
    {
        private readonly HttpContent _content;
        private readonly Uri? _effectiveUri;

        public FirstResponseHandler(
            string content,
            string mediaType,
            Uri? effectiveUri)
            : this(
                new StringContent(content, Encoding.UTF8, mediaType),
                effectiveUri)
        {
        }

        public FirstResponseHandler(HttpContent content, Uri? effectiveUri)
        {
            _content = content;
            _effectiveUri = effectiveUri;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = _effectiveUri is null
                    ? null
                    : new HttpRequestMessage(HttpMethod.Get, _effectiveUri),
                Content = _content,
            });
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
}
