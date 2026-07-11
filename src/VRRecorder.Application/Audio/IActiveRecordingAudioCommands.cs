namespace VRRecorder.Application.Audio;

public interface IActiveRecordingAudioCommands
{
    RecordingAudioControlState? CurrentAudioControlState { get; }

    Task<RecordingAudioControlState> ExecuteAudioCommandAsync(
        RecordingAudioCommand command,
        CancellationToken cancellationToken);
}
