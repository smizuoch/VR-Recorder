namespace VRRecorder.Application.Audio;

public sealed record DesktopAudioEndpointOptions(
    IReadOnlyList<AudioEndpointOption> Desktop,
    IReadOnlyList<AudioEndpointOption> Microphone);
