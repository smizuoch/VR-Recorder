namespace VRRecorder.Application.Ports;

public interface IVideoSenderSelectionStore
{
    Task<string?> LoadAsync(
        string vrChatServiceId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        string vrChatServiceId,
        string senderId,
        CancellationToken cancellationToken);
}
