namespace VRRecorder.Application.Haptics;

public sealed record WristHapticFeedbackOptions
{
    public WristHapticFeedbackOptions(
        bool isEnabled,
        float frequencyHertz,
        float amplitude)
    {
        WristHapticPattern.ValidateFrequency(frequencyHertz);
        WristHapticPattern.ValidateAmplitude(amplitude);
        IsEnabled = isEnabled;
        FrequencyHertz = frequencyHertz;
        Amplitude = amplitude;
    }

    public bool IsEnabled { get; }

    public float FrequencyHertz { get; }

    public float Amplitude { get; }
}
