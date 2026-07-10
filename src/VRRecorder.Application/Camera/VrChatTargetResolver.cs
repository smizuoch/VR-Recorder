using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Camera;

public sealed class VrChatTargetResolver
{
    private readonly IVrChatInstanceDiscovery _discovery;

    public VrChatTargetResolver(IVrChatInstanceDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        _discovery = discovery;
    }

    public async Task<VrChatTargetResolution> ResolveAsync(
        string? selectedServiceId,
        CancellationToken cancellationToken)
    {
        var discovered = await _discovery
            .DiscoverAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(discovered);
        var candidates = discovered
            .OrderBy(candidate => candidate.ServiceId, StringComparer.Ordinal)
            .ToArray();
        var duplicate = candidates
            .GroupBy(candidate => candidate.ServiceId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"VRChat service identity {duplicate.Key} was discovered more than once.");
        }

        if (candidates.Length == 0)
        {
            return new VrChatTargetResolution.NotFound();
        }

        if (selectedServiceId is not null)
        {
            var selected = candidates.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.ServiceId,
                    selectedServiceId,
                    StringComparison.Ordinal));
            return selected is null
                ? new VrChatTargetResolution.SelectionRequired(candidates)
                : new VrChatTargetResolution.Selected(selected);
        }

        return candidates.Length == 1
            ? new VrChatTargetResolution.Selected(candidates[0])
            : new VrChatTargetResolution.SelectionRequired(candidates);
    }
}
