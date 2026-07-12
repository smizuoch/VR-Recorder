using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Diagnostics;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Osc;

public sealed class OscQueryVrChatInstanceDiscovery
    : IVrChatInstanceDiscovery
{
    private const int MaximumResponseBytes = 64 * 1024;
    private const int MaximumJsonDepth = 16;
    private const string VrChatServicePrefix = "VRChat-Client-";
    private static readonly MediaTypeWithQualityHeaderValue JsonMediaType =
        new("application/json");
    private readonly IOscQueryServiceBrowser _browser;
    private readonly HttpMessageInvoker _http;
    private readonly TimeSpan _timeout;
    private readonly IOscOperationEventSink? _events;

    public OscQueryVrChatInstanceDiscovery(
        IOscQueryServiceBrowser browser,
        HttpMessageInvoker http,
        TimeSpan timeout)
        : this(browser, http, timeout, events: null)
    {
    }

    public OscQueryVrChatInstanceDiscovery(
        IOscQueryServiceBrowser browser,
        HttpMessageInvoker http,
        TimeSpan timeout,
        IOscOperationEventSink? events)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(http);
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The OSCQuery timeout must be positive and finite.");
        }

        _browser = browser;
        _http = http;
        _timeout = timeout;
        _events = events;
    }

    public async Task<IReadOnlyList<VrChatInstanceCandidate>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            var advertisements = await _browser
                .BrowseAsync(timeout.Token)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(advertisements);
            var probes = advertisements
                .Where(IsEligibleAdvertisement)
                .Select(advertisement =>
                    ProbeAsync(advertisement, timeout.Token));
            var candidates = await Task.WhenAll(probes).ConfigureAwait(false);
            var result = candidates
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .OrderBy(
                    candidate => candidate.ServiceId,
                    StringComparer.Ordinal)
                .ToArray();
            PublishBestEffort(
                result.Length > 0
                    ? OscOperationOutcome.Succeeded
                    : OscOperationOutcome.Failed);
            return result;
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested &&
            timeout.IsCancellationRequested)
        {
            PublishBestEffort(OscOperationOutcome.Failed);
            throw new OscQueryTimeoutException(_timeout, exception);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            PublishBestEffort(OscOperationOutcome.Failed);
            throw;
        }
    }

    private void PublishBestEffort(OscOperationOutcome outcome)
    {
        try
        {
            _events?.Publish(new OscOperationEvent(
                OscOperation.CapabilityProbe,
                outcome));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning(
                "OSC capability diagnostics failed: {0}",
                exception.GetType().Name);
        }
    }

    private static bool IsEligibleAdvertisement(
        OscQueryServiceAdvertisement advertisement) =>
        advertisement is not null &&
        advertisement.InstanceName.StartsWith(
            VrChatServicePrefix,
            StringComparison.Ordinal) &&
        IPAddress.IsLoopback(advertisement.Address);

    private async Task<VrChatInstanceCandidate?> ProbeAsync(
        OscQueryServiceAdvertisement advertisement,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProbeTrustedEndpointAsync(
                    advertisement,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UntrustedResponseUriException)
        {
            return null;
        }
    }

    private async Task<VrChatInstanceCandidate?> ProbeTrustedEndpointAsync(
        OscQueryServiceAdvertisement advertisement,
        CancellationToken cancellationToken)
    {
        var queryEndpoint = new UriBuilder(
            Uri.UriSchemeHttp,
            advertisement.Address.ToString(),
            advertisement.HttpPort,
            "/").Uri;
        var host = await ReadHostInfoAsync(
                queryEndpoint,
                cancellationToken)
            .ConfigureAwait(false);
        if (host is null ||
            !string.Equals(
                host.Name,
                advertisement.InstanceName,
                StringComparison.Ordinal))
        {
            return null;
        }

        var capabilityChecks = new[]
        {
            ReadEndpointAsync(
                queryEndpoint,
                advertisement.ServiceId,
                "/usercamera/Mode",
                expectedType: "i",
                cancellationToken),
            ReadBooleanEndpointAsync(
                queryEndpoint,
                advertisement.ServiceId,
                "/usercamera/Streaming",
                cancellationToken),
            ReadBooleanEndpointAsync(
                queryEndpoint,
                advertisement.ServiceId,
                "/usercamera/OrientationIsLandscape",
                cancellationToken),
        };
        if ((await Task.WhenAll(capabilityChecks).ConfigureAwait(false))
            .Any(isValid => !isValid))
        {
            return null;
        }

        return new VrChatInstanceCandidate(
            advertisement.ServiceId,
            host.Name,
            queryEndpoint,
            host.OscAddress.ToString(),
            host.OscPort);
    }

    private async Task<HostInfo?> ReadHostInfoAsync(
        Uri queryEndpoint,
        CancellationToken cancellationToken)
    {
        using var document = await ReadJsonAsync(
                new Uri(queryEndpoint, "?HOST_INFO"),
                cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty(
                "HOST_INFO",
                out var hostInfo) ||
            hostInfo.ValueKind != JsonValueKind.Object ||
            !TryReadString(hostInfo, "NAME", out var name) ||
            !TryReadString(hostInfo, "OSC_IP", out var oscIp) ||
            !IPAddress.TryParse(oscIp, out var oscAddress) ||
            !IPAddress.IsLoopback(oscAddress) ||
            !TryReadInt32(hostInfo, "OSC_PORT", out var oscPort) ||
            oscPort is < 1 or > 65535)
        {
            return null;
        }

        if (hostInfo.TryGetProperty("OSC_TRANSPORT", out var transport) &&
            (transport.ValueKind != JsonValueKind.String ||
             !string.Equals(
                 transport.GetString(),
                 "UDP",
                 StringComparison.Ordinal)))
        {
            return null;
        }

        return new HostInfo(name, oscAddress, oscPort);
    }

    private Task<bool> ReadBooleanEndpointAsync(
        Uri queryEndpoint,
        string serviceId,
        string path,
        CancellationToken cancellationToken) =>
        ReadEndpointAsync(
            queryEndpoint,
            serviceId,
            path,
            expectedType: null,
            cancellationToken);

    private async Task<bool> ReadEndpointAsync(
        Uri queryEndpoint,
        string serviceId,
        string path,
        string? expectedType,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await ReadJsonAsync(
                    new Uri(queryEndpoint, path),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new VrChatCameraEndpointMissingException(
                serviceId,
                path,
                exception);
        }

        using (document)
        {
            return IsCompatibleEndpoint(document.RootElement, path, expectedType);
        }
    }

    private static bool IsCompatibleEndpoint(
        JsonElement root,
        string path,
        string? expectedType)
    {
        if (!TryReadString(root, "FULL_PATH", out var fullPath) ||
            !string.Equals(fullPath, path, StringComparison.Ordinal) ||
            !TryReadString(root, "TYPE", out var type) ||
            !TryReadInt32(root, "ACCESS", out var access) ||
            (access & 3) != 3)
        {
            return false;
        }

        return expectedType is null
            ? type is "T" or "F"
            : string.Equals(type, expectedType, StringComparison.Ordinal);
    }

    private async Task<JsonDocument> ReadJsonAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(JsonMediaType);
        using var response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        EnsureTrustedResponseUri(
            endpoint,
            response.RequestMessage?.RequestUri);
        response.EnsureSuccessStatusCode();
        if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "OSCQuery returned a non-JSON response.");
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "OSCQuery response exceeds the 64 KiB limit.");
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
                "OSCQuery response exceeds the 64 KiB limit.");
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
                "OSCQuery JSON contains duplicate properties.");
        }

        return document;
    }

    private static void EnsureTrustedResponseUri(
        Uri requestedEndpoint,
        Uri? effectiveEndpoint)
    {
        if (effectiveEndpoint is null ||
            !effectiveEndpoint.IsAbsoluteUri ||
            !string.Equals(
                effectiveEndpoint.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                effectiveEndpoint.Authority,
                requestedEndpoint.Authority,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(effectiveEndpoint.UserInfo) ||
            !IPAddress.TryParse(
                effectiveEndpoint.IdnHost,
                out var effectiveAddress) ||
            !IPAddress.IsLoopback(effectiveAddress))
        {
            throw new UntrustedResponseUriException();
        }
    }

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

    private sealed record HostInfo(
        string Name,
        IPAddress OscAddress,
        int OscPort);

    private sealed class UntrustedResponseUriException : Exception
    {
    }
}
