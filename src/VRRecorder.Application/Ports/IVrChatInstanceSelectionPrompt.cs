using VRRecorder.Application.Camera;

namespace VRRecorder.Application.Ports;

public interface IVrChatInstanceSelectionPrompt
{
    Task<string?> SelectAsync(
        IReadOnlyList<VrChatInstanceCandidate> candidates,
        CancellationToken cancellationToken);
}
