using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Ports;

public interface IFirstRunSetupStore
{
    Task<FirstRunSetupProgress?> LoadAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        FirstRunSetupProgress progress,
        CancellationToken cancellationToken);
}
