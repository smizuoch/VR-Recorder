using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface IRecordingPlaybackLauncher
{
    Task<bool> StartAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken);
}
