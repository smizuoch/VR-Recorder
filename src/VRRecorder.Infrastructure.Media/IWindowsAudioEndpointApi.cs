using VRRecorder.Domain.Audio;

namespace VRRecorder.Infrastructure.Media;

public interface IWindowsAudioEndpointApi
{
    IReadOnlyList<WindowsAudioEndpoint> EnumerateActive(AudioInput input);
}
