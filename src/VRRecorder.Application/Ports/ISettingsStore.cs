using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Ports;

public interface ISettingsStore
{
    Task<VRRecorderSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        VRRecorderSettings settings,
        CancellationToken cancellationToken);
}
