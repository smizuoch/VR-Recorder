namespace VRRecorder.Domain.Audio;

public static class AudioMixer
{
    public static float[] Mix(
        float[] desktop,
        float[] microphone,
        AudioGains gains)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(microphone);
        return Mix(
            desktop,
            microphone,
            desktop.Length,
            gains,
            AudioInputAvailability.All);
    }

    public static float[] Mix(
        float[] desktop,
        float[] microphone,
        int scheduledSampleCount,
        AudioGains gains,
        AudioInputAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(microphone);
        if (scheduledSampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduledSampleCount),
                scheduledSampleCount,
                "The scheduled sample count cannot be negative.");
        }

        if (!Enum.IsDefined(availability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(availability),
                availability,
                "Unknown audio input availability.");
        }

        EnsureAvailableBufferLength(
            desktop,
            scheduledSampleCount,
            availability.HasFlag(AudioInputAvailability.Desktop),
            nameof(desktop));
        EnsureAvailableBufferLength(
            microphone,
            scheduledSampleCount,
            availability.HasFlag(AudioInputAvailability.Microphone),
            nameof(microphone));

        var desktopAvailable = availability.HasFlag(
            AudioInputAvailability.Desktop);
        var microphoneAvailable = availability.HasFlag(
            AudioInputAvailability.Microphone);
        var output = new float[scheduledSampleCount];
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = (float)(
                ((desktopAvailable ? desktop[index] : 0) * gains.Desktop) +
                ((microphoneAvailable ? microphone[index] : 0) *
                 gains.Microphone));
        }

        return output;
    }

    private static void EnsureAvailableBufferLength(
        float[] buffer,
        int scheduledSampleCount,
        bool isAvailable,
        string parameterName)
    {
        if (isAvailable && buffer.Length != scheduledSampleCount)
        {
            throw new ArgumentException(
                "An available audio input must provide the scheduled sample count.",
                parameterName);
        }
    }
}
