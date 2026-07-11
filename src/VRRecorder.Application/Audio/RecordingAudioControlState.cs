using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public sealed record RecordingAudioControlState(
    bool DesktopIncluded,
    bool MicrophoneIncluded,
    bool MuteAll)
{
    public AudioRouting EffectiveRouting => MuteAll
        ? AudioRouting.Muted
        : (DesktopIncluded, MicrophoneIncluded) switch
        {
            (true, true) => AudioRouting.Mixed,
            (true, false) => AudioRouting.DesktopOnly,
            (false, true) => AudioRouting.MicOnly,
            (false, false) => AudioRouting.Muted,
        };

    public static RecordingAudioControlState FromRouting(AudioRouting routing) =>
        routing switch
        {
            AudioRouting.Mixed => new(true, true, false),
            AudioRouting.DesktopOnly => new(true, false, false),
            AudioRouting.MicOnly => new(false, true, false),
            AudioRouting.Muted => new(true, true, true),
            _ => throw new ArgumentOutOfRangeException(
                nameof(routing),
                routing,
                "Unknown audio routing."),
        };

    public RecordingAudioControlState Apply(RecordingAudioCommand command) =>
        command switch
        {
            RecordingAudioCommand.ToggleMicrophone => this with
            {
                MicrophoneIncluded = !MicrophoneIncluded,
            },
            RecordingAudioCommand.ToggleMuteAll => this with
            {
                MuteAll = !MuteAll,
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(command),
                command,
                "Unknown recording audio command."),
        };
}
