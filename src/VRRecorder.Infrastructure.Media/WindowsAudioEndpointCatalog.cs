using VRRecorder.Application.Audio;
using System.Runtime.Versioning;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Infrastructure.Media;

public sealed class WindowsAudioEndpointCatalog : IAudioEndpointCatalog
{
    private readonly IWindowsAudioEndpointApi _api;

    [SupportedOSPlatform("windows")]
    public WindowsAudioEndpointCatalog()
        : this(new ShellWindowsAudioEndpointApi())
    {
    }

    public WindowsAudioEndpointCatalog(IWindowsAudioEndpointApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Task<IReadOnlyList<AudioEndpointOption>> GetActiveAsync(
        AudioInput input,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(input))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var endpoints = _api.EnumerateActive(input);
        ArgumentNullException.ThrowIfNull(endpoints);
        IReadOnlyList<AudioEndpointOption> result = endpoints
            .Select(endpoint =>
            {
                ArgumentNullException.ThrowIfNull(endpoint);
                return new AudioEndpointOption(
                    endpoint.Id,
                    endpoint.DisplayName);
            })
            .OrderBy(option => option.DisplayName, StringComparer.Ordinal)
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(result);
    }
}
