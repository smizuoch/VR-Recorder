using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IVideoSenderSelectionPrompt
{
    Task<string?> SelectAsync(
        IReadOnlyList<StableVideoSignal> candidates,
        CancellationToken cancellationToken);
}
