using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VRRecorder.Application.Camera;
using VRRecorder.Domain.Camera;

namespace VRRecorder.Infrastructure.Osc;

internal sealed class OscQueryCameraSnapshotReader
{
    private const int MaximumResponseBytes = 64 * 1024;
    private const int MaximumJsonDepth = 16;
    private static readonly MediaTypeWithQualityHeaderValue JsonMediaType =
        new("application/json");
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(1);
    private readonly VrChatInstanceCandidate _candidate;
    private readonly HttpMessageInvoker _http;
    private readonly IPAddress _expectedOscAddress;

    public OscQueryCameraSnapshotReader(
        VrChatInstanceCandidate candidate,
        HttpMessageInvoker http)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(http);
        if (!IsTrustedLoopbackEndpoint(candidate.OscQueryEndpoint) ||
            !string.Equals(
                candidate.OscQueryEndpoint.AbsolutePath,
                "/",
                StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(candidate.OscQueryEndpoint.Query) ||
            !string.IsNullOrEmpty(candidate.OscQueryEndpoint.Fragment))
        {
            throw new ArgumentException(
                "The selected OSCQuery endpoint must be an HTTP loopback root.",
                nameof(candidate));
        }

        if (!IPAddress.TryParse(
                candidate.OscHost,
                out var expectedOscAddress) ||
            !IPAddress.IsLoopback(expectedOscAddress))
        {
            throw new ArgumentException(
                "The selected VRChat OSC address must be loopback.",
                nameof(candidate));
        }

        _expectedOscAddress = expectedOscAddress;
        _candidate = candidate;
        _http = http;
    }

    public async Task<CameraSnapshot> ReadAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(SnapshotTimeout);
        await VerifySelectedHostAsync(timeout.Token).ConfigureAwait(false);
        var mode = await ReadModeAsync(timeout.Token).ConfigureAwait(false);
        var streaming = await ReadStreamingAsync(timeout.Token)
            .ConfigureAwait(false);
        return new CameraSnapshot(
            ObservedCameraValue.Known(mode),
            ObservedCameraValue.Known(streaming));
    }

    private async Task VerifySelectedHostAsync(
        CancellationToken cancellationToken)
    {
        using var document = await ReadJsonAsync(
                new Uri(_candidate.OscQueryEndpoint, "?HOST_INFO"),
                cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty(
                "HOST_INFO",
                out var hostInfo) ||
            hostInfo.ValueKind != JsonValueKind.Object ||
            !TryReadString(hostInfo, "NAME", out var name) ||
            !string.Equals(
                name,
                _candidate.DisplayName,
                StringComparison.Ordinal) ||
            !TryReadString(hostInfo, "OSC_IP", out var oscIp) ||
            !IPAddress.TryParse(oscIp, out var oscAddress) ||
            !oscAddress.Equals(_expectedOscAddress) ||
            !TryReadInt32(hostInfo, "OSC_PORT", out var oscPort) ||
            oscPort != _candidate.OscPort ||
            (hostInfo.TryGetProperty("OSC_TRANSPORT", out var transport) &&
             (transport.ValueKind != JsonValueKind.String ||
              !string.Equals(
                  transport.GetString(),
                  "UDP",
                  StringComparison.Ordinal))))
        {
            throw new InvalidDataException(
                "The selected OSCQuery service identity changed before snapshot read.");
        }
    }

    private async Task<CameraMode> ReadModeAsync(
        CancellationToken cancellationToken)
    {
        const string path = "/usercamera/Mode";
        using var document = await ReadJsonAsync(
                new Uri(_candidate.OscQueryEndpoint, path),
                cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (!HasEndpointContract(root, path, "i") ||
            !root.TryGetProperty("VALUE", out var values) ||
            values.ValueKind != JsonValueKind.Array ||
            values.GetArrayLength() != 1 ||
            !values[0].TryGetInt32(out var rawMode) ||
            !Enum.IsDefined((CameraMode)rawMode))
        {
            throw new InvalidDataException(
                "OSCQuery returned an invalid camera mode snapshot.");
        }

        return (CameraMode)rawMode;
    }

    private async Task<bool> ReadStreamingAsync(
        CancellationToken cancellationToken)
    {
        const string path = "/usercamera/Streaming";
        using var document = await ReadJsonAsync(
                new Uri(_candidate.OscQueryEndpoint, path),
                cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (!TryReadString(root, "TYPE", out var type) ||
            type is not ("T" or "F") ||
            !HasEndpointContract(root, path, type))
        {
            throw new InvalidDataException(
                "OSCQuery returned an invalid camera streaming snapshot.");
        }

        var expected = string.Equals(type, "T", StringComparison.Ordinal);
        if (root.TryGetProperty("VALUE", out var values) &&
            !IsCompatibleBooleanValue(values, expected))
        {
            throw new InvalidDataException(
                "OSCQuery returned inconsistent camera streaming values.");
        }

        return expected;
    }

    private static bool IsCompatibleBooleanValue(
        JsonElement values,
        bool expected)
    {
        if (values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (values.GetArrayLength() == 0)
        {
            return true;
        }

        return values.GetArrayLength() == 1 &&
               values[0].ValueKind is JsonValueKind.True or JsonValueKind.False &&
               values[0].GetBoolean() == expected;
    }

    private static bool HasEndpointContract(
        JsonElement root,
        string path,
        string expectedType) =>
        TryReadString(root, "FULL_PATH", out var fullPath) &&
        string.Equals(fullPath, path, StringComparison.Ordinal) &&
        TryReadString(root, "TYPE", out var type) &&
        string.Equals(type, expectedType, StringComparison.Ordinal) &&
        TryReadInt32(root, "ACCESS", out var access) &&
        (access & 3) == 3;

    private async Task<JsonDocument> ReadJsonAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(JsonMediaType);
        using var response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        EnsureTrustedResponseUri(endpoint, response.RequestMessage?.RequestUri);
        response.EnsureSuccessStatusCode();
        if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "OSCQuery returned a non-JSON snapshot response.");
        }

        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "OSCQuery snapshot response exceeds the 64 KiB limit.");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var buffer = new byte[MaximumResponseBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "OSCQuery snapshot response exceeds the 64 KiB limit.");
        }

        var document = JsonDocument.Parse(
            buffer.AsMemory(0, length),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumJsonDepth,
            });
        if (HasDuplicateProperties(document.RootElement))
        {
            document.Dispose();
            throw new InvalidDataException(
                "OSCQuery snapshot JSON contains duplicate properties.");
        }

        return document;
    }

    private static void EnsureTrustedResponseUri(
        Uri requestedEndpoint,
        Uri? effectiveEndpoint)
    {
        if (effectiveEndpoint is null ||
            !IsTrustedLoopbackEndpoint(effectiveEndpoint) ||
            !string.Equals(
                effectiveEndpoint.Authority,
                requestedEndpoint.Authority,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                effectiveEndpoint.PathAndQuery,
                requestedEndpoint.PathAndQuery,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "OSCQuery snapshot response came from an untrusted endpoint.");
        }
    }

    private static bool IsTrustedLoopbackEndpoint(Uri endpoint) =>
        endpoint.IsAbsoluteUri &&
        string.Equals(
            endpoint.Scheme,
            Uri.UriSchemeHttp,
            StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(endpoint.UserInfo) &&
        IPAddress.TryParse(endpoint.IdnHost, out var address) &&
        IPAddress.IsLoopback(address);

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperties);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name) ||
                HasDuplicateProperties(property.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    private static bool TryReadInt32(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }
}
