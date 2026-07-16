namespace VRRecorder.Application.Haptics;

public sealed record WristHapticPattern
{
    public WristHapticPattern(
        TimeSpan duration,
        int pulseCount,
        float frequencyHertz,
        float amplitude)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Haptic duration must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(pulseCount, 1);
        ValidateFrequency(frequencyHertz);
        ValidateAmplitude(amplitude);

        Duration = duration;
        PulseCount = pulseCount;
        FrequencyHertz = frequencyHertz;
        Amplitude = amplitude;
    }

    public TimeSpan Duration { get; }

    public int PulseCount { get; }

    public float FrequencyHertz { get; }

    public float Amplitude { get; }

    internal static void ValidateFrequency(float frequencyHertz)
    {
        if (!float.IsFinite(frequencyHertz) || frequencyHertz < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frequencyHertz),
                frequencyHertz,
                "Haptic frequency must be finite and non-negative.");
        }
    }

    internal static void ValidateAmplitude(float amplitude)
    {
        if (!float.IsFinite(amplitude) || amplitude <= 0 || amplitude > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amplitude),
                amplitude,
                "Haptic amplitude must be greater than zero and at most one.");
        }
    }
}
