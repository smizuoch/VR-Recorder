namespace VRRecorder.Application.Audio;

public sealed record MixedStereoAudioBuffer(
    long StartFrame,
    int SampleRate,
    int ChannelCount,
    float[] InterleavedSamples);
