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

    public static float[] MixInterleaved(
        float[] desktop,
        float[] microphone,
        int scheduledFrameCount,
        int channelCount,
        AudioRoutingRamp ramp,
        int rampStartFrame,
        AudioInputAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(microphone);
        ArgumentNullException.ThrowIfNull(ramp);
        if (scheduledFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduledFrameCount),
                scheduledFrameCount,
                "The scheduled frame count cannot be negative.");
        }

        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelCount),
                channelCount,
                "The channel count must be positive.");
        }

        if (rampStartFrame < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rampStartFrame),
                rampStartFrame,
                "The ramp start frame cannot be negative.");
        }

        if (!Enum.IsDefined(availability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(availability),
                availability,
                "Unknown audio input availability.");
        }

        var scheduledSampleCount = checked(scheduledFrameCount * channelCount);
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
            var frameOffset = index / channelCount;
            var gains = ramp.AtSample(checked(rampStartFrame + frameOffset));
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
