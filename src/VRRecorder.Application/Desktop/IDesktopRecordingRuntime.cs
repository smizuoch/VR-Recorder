using VRRecorder.Application.Audio;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Desktop;

public interface IDesktopRecordingRuntime
    : IAsyncDisposable,
      IRecorderStatusSource,
      IActiveRecordingAudioCommands
{
    RecordingAudioControlState?
        IActiveRecordingAudioCommands.CurrentAudioControlState =>
        Current.AudioControlState;

    Task<RecordingAudioControlState>
        IActiveRecordingAudioCommands.ExecuteAudioCommandAsync(
            RecordingAudioCommand command,
            CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "The desktop recording runtime does not support live audio commands.");

    Task ToggleAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(RecordingStopReason reason);
}
