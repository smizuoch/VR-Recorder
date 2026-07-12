using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Video;

public sealed class CachedPromptingVideoSenderSelection
    : IVideoSenderSelection
{
    private readonly IVideoSenderSelectionStore _store;
    private readonly IVideoSenderSelectionPrompt _prompt;

    public CachedPromptingVideoSenderSelection(
        IVideoSenderSelectionStore store,
        IVideoSenderSelectionPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(prompt);
        _store = store;
        _prompt = prompt;
    }

    public async Task<string?> SelectAsync(
        string vrChatServiceId,
        IReadOnlyList<StableVideoSignal> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        ArgumentNullException.ThrowIfNull(candidates);
        cancellationToken.ThrowIfCancellationRequested();
        var ordered = candidates
            .OrderBy(candidate => candidate?.SenderId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0 || ordered.Any(candidate => candidate is null))
        {
            throw new InvalidDataException(
                "Video sender selection requires at least one valid candidate.");
        }

        if (ordered
            .GroupBy(candidate => candidate.SenderId, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            throw new InvalidDataException(
                "Video sender selection candidates must have unique sender IDs.");
        }

        var cachedSenderId = await _store
            .LoadAsync(vrChatServiceId, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var cached = ordered.SingleOrDefault(candidate => string.Equals(
            candidate.SenderId,
            cachedSenderId,
            StringComparison.Ordinal));
        if (cached is not null)
        {
            return cached.SenderId;
        }

        if (ordered.Length == 1)
        {
            await _store
                .SaveAsync(
                    vrChatServiceId,
                    ordered[0].SenderId,
                    cancellationToken)
                .ConfigureAwait(false);
            return ordered[0].SenderId;
        }

        var selectedSenderId = await _prompt
            .SelectAsync(ordered, cancellationToken)
            .ConfigureAwait(false);
        if (selectedSenderId is null)
        {
            return null;
        }

        var selected = ordered.SingleOrDefault(candidate => string.Equals(
            candidate.SenderId,
            selectedSenderId,
            StringComparison.Ordinal));
        if (selected is null)
        {
            throw new InvalidDataException(
                "The selected video sender is not an offered candidate.");
        }

        await _store
            .SaveAsync(
                vrChatServiceId,
                selected.SenderId,
                cancellationToken)
            .ConfigureAwait(false);
        return selected.SenderId;
    }
}
