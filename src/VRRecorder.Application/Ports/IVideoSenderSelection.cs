using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Ports;

public interface IVideoSenderSelection
{
    Task<string?> SelectAsync(
        string vrChatServiceId,
        IReadOnlyList<StableVideoSignal> candidates,
        CancellationToken cancellationToken);
}
