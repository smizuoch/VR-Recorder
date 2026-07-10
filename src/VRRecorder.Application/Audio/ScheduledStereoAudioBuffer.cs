using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public sealed record ScheduledStereoAudioBuffer(
    long StartFrame,
    int SampleRate,
    int ScheduledFrameCount,
    float[] DesktopInterleavedSamples,
    float[] MicrophoneInterleavedSamples,
    AudioInputAvailability Availability);
